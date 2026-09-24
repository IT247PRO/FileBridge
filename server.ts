import express, { Request, Response } from 'express';
import session from 'express-session';
import cookieParser from 'cookie-parser';
import multer from 'multer';
import path from 'path';
import {
  store,
  EndpointTypeNames,
  TransferDirectionNames,
  EncryptionOperationNames,
  ScheduleTypeNames,
  DuplicatePolicyNames,
  Job
} from './src/store.js';

const app = express();
const upload = multer({ storage: multer.memoryStorage() });

// Middleware
app.use(express.urlencoded({ extended: true }));
app.use(express.json());
app.use(cookieParser());
app.use(
  session({
    secret: process.env.SESSION_SECRET || 'filebridge-session-secret-key-12345',
    resave: false,
    saveUninitialized: true
  }) as any
);

// Static files
app.use(express.static(path.join(process.cwd(), 'public')));

// Template engine
app.set('views', path.join(process.cwd(), 'views'));
app.set('view engine', 'ejs');

// Global template variables middleware
app.use((req: Request, res: Response, next) => {
  const sess = req.session as any;
  res.locals.csrfToken = 'fb-csrf-token-' + (sess?.id || 'default');
  res.locals.flashMessage = sess?.flashMessage || null;
  res.locals.currentUser = sess?.user || 'AGENCY\\admin';
  sess.flashMessage = null; // consume flash
  next();
});

// Helper flash setter
function setFlash(req: Request, msg: string) {
  (req.session as any).flashMessage = msg;
}

// -------------------------------------------------------------
// Health Check Routes
// -------------------------------------------------------------
app.get('/health/live', (_req: Request, res: Response) => {
  res.json({ status: 'Live', timeUtc: new Date().toISOString() });
});

app.get('/health/ready', (_req: Request, res: Response) => {
  res.json({ status: 'Ready', storage: 'InMemoryStore', timeUtc: new Date().toISOString() });
});

// -------------------------------------------------------------
// Dashboard Routes
// -------------------------------------------------------------
app.get('/', (_req: Request, res: Response) => {
  const todayUtc = new Date();
  todayUtc.setUTCHours(0, 0, 0, 0);

  const succeededHist = store.histories.filter(
    h => h.transferStatusId === 'Succeeded' && new Date(h.startedUtc) >= todayUtc
  );
  const failedHist = store.histories.filter(
    h => h.transferStatusId === 'Failed' && new Date(h.startedUtc) >= todayUtc
  );
  const inProgressCount = store.histories.filter(h => h.transferStatusId === 'InProgress').length;
  const bytesToday = succeededHist.reduce((acc, h) => acc + (h.sizeBytes || 0), 0);
  const heldQuarantine = store.quarantines.filter(q => q.quarantineStatusId === 1).length;
  const pendingApprovals = store.changeRequests.filter(c => c.approvalStatusId === 1).length;

  const failingJobsMap = new Map<string, { jobName: string; failed: number; lastFailureUtc: string }>();
  for (const f of failedHist) {
    const job = store.jobs.find(j => j.id === f.jobId);
    const jName = job?.name || 'Unknown Job';
    const existing = failingJobsMap.get(jName) || { jobName: jName, failed: 0, lastFailureUtc: f.startedUtc };
    existing.failed++;
    if (new Date(f.startedUtc) > new Date(existing.lastFailureUtc)) {
      existing.lastFailureUtc = f.startedUtc;
    }
    failingJobsMap.set(jName, existing);
  }

  const vm = {
    succeededToday: succeededHist.length,
    failedToday: failedHist.length,
    inProgress: inProgressCount,
    bytesToday,
    heldQuarantine,
    pendingApprovals,
    killSwitchOn: store.getKillSwitch(),
    nodes: store.nodeHeartbeats,
    recentFailingJobs: Array.from(failingJobsMap.values())
  };

  res.render('home', {
    title: 'Dashboard',
    activeNav: 'dashboard',
    vm
  });
});

app.post('/Home/ToggleKillSwitch', (req: Request, res: Response) => {
  const current = store.getKillSwitch();
  store.setKillSwitch(!current);
  setFlash(req, !current ? 'Kill switch is now ON cluster-wide.' : 'Kill switch turned OFF.');
  res.redirect('/');
});

// -------------------------------------------------------------
// Jobs Routes
// -------------------------------------------------------------
app.get('/Jobs', (_req: Request, res: Response) => {
  const jobsWithEndpoints = store.jobs.map(j => ({
    ...j,
    sourceEndpoint: store.endpoints.find(e => e.id === j.sourceEndpointId),
    destinationEndpoint: store.endpoints.find(e => e.id === j.destinationEndpointId)
  }));

  res.render('jobs/index', {
    title: 'Jobs',
    activeNav: 'jobs',
    jobs: jobsWithEndpoints
  });
});

app.get('/Jobs/Create', (_req: Request, res: Response) => {
  const emptyJob: Partial<Job> = {
    id: 0,
    name: '',
    transferDirectionId: 1,
    sourceEndpointId: store.endpoints[0]?.id || 1,
    destinationEndpointId: store.endpoints[1]?.id || 2,
    isEnabled: true,
    isPaused: false,
    scheduleTypeId: 2,
    intervalSeconds: 300,
    timeZoneId: 'Central Standard Time',
    activeDaysMask: 127,
    activeDays: [0, 1, 2, 3, 4, 5, 6],
    stabilitySeconds: 30,
    maxParallelFiles: 4,
    maxRetries: 3,
    retryBaseSeconds: 30,
    duplicatePolicyId: 3,
    compressionOperationId: 1,
    useTempNameOnUpload: true,
    verifyAfterUpload: true,
    folderMaps: [{ id: 1, sourcePath: '', destinationPath: '', recursive: false, preserveSubfolders: false }],
    filters: [],
    semaphore: { semaphoreModeId: 1, triggerPattern: '{filename}.done', deleteTriggerAfter: true, transferTrigger: false },
    postAction: { postActionTypeId: 1, archivePath: 'archive', renamePattern: '' },
    notifications: []
  };

  res.render('jobs/edit', {
    title: 'Add job',
    activeNav: 'jobs',
    model: emptyJob,
    endpoints: store.endpoints,
    profiles: store.encryptionProfiles,
    errors: []
  });
});

app.get('/Jobs/Edit/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const job = store.jobs.find(j => j.id === id);
  if (!job) {
    return res.status(404).send('Job not found');
  }

  res.render('jobs/edit', {
    title: 'Edit job',
    activeNav: 'jobs',
    model: job,
    endpoints: store.endpoints,
    profiles: store.encryptionProfiles,
    errors: []
  });
});

app.post('/Jobs/Save', (req: Request, res: Response) => {
  const body = req.body;
  const id = parseInt(body.id, 10) || 0;
  const errors: string[] = [];

  if (!body.name || !body.name.trim()) errors.push('Name is required.');
  const srcId = parseInt(body.sourceEndpointId, 10);
  const dstId = parseInt(body.destinationEndpointId, 10);
  if (!srcId || !dstId) errors.push('Source and destination endpoints are required.');
  if (srcId === dstId) errors.push('Source and destination endpoints must be different.');

  if (errors.length > 0) {
    return res.render('jobs/edit', {
      title: id ? 'Edit job' : 'Add job',
      activeNav: 'jobs',
      model: body,
      endpoints: store.endpoints,
      profiles: store.encryptionProfiles,
      errors
    });
  }

  // Parse nested folder maps
  const folderMaps: any[] = [];
  if (body.folderMaps) {
    const keys = Object.keys(body.folderMaps);
    for (const k of keys) {
      const item = body.folderMaps[k];
      if (item && (item.sourcePath || item.destinationPath)) {
        folderMaps.push({
          id: parseInt(item.id, 10) || folderMaps.length + 1,
          sourcePath: item.sourcePath || '',
          destinationPath: item.destinationPath || '',
          recursive: item.recursive === 'true' || item.recursive === true,
          preserveSubfolders: item.preserveSubfolders === 'true' || item.preserveSubfolders === true
        });
      }
    }
  }

  // Parse nested filters
  const filters: any[] = [];
  if (body.filters) {
    const keys = Object.keys(body.filters);
    for (const k of keys) {
      const item = body.filters[k];
      if (item && item.pattern) {
        filters.push({
          id: parseInt(item.id, 10) || filters.length + 1,
          pattern: item.pattern,
          isRegex: item.isRegex === 'true' || item.isRegex === true,
          isExclude: item.isExclude === 'true' || item.isExclude === true,
          minSizeBytes: item.minSizeBytes ? parseInt(item.minSizeBytes, 10) : undefined,
          maxSizeBytes: item.maxSizeBytes ? parseInt(item.maxSizeBytes, 10) : undefined,
          minAgeSeconds: item.minAgeSeconds ? parseInt(item.minAgeSeconds, 10) : undefined,
          allowedFileTypes: item.allowedFileTypes || undefined
        });
      }
    }
  }

  // Parse notifications
  const notifications: any[] = [];
  if (body.notifications) {
    const keys = Object.keys(body.notifications);
    for (const k of keys) {
      const item = body.notifications[k];
      if (item && item.target) {
        notifications.push({
          id: parseInt(item.id, 10) || notifications.length + 1,
          notificationEventId: item.notificationEventId || 'JobFailed',
          notificationChannelId: item.notificationChannelId || 'Email',
          target: item.target,
          isEnabled: item.isEnabled === 'true' || item.isEnabled === true
        });
      }
    }
  }

  // Active days
  let activeDays: number[] = [0, 1, 2, 3, 4, 5, 6];
  if (body.activeDays) {
    activeDays = Array.isArray(body.activeDays) ? body.activeDays.map(Number) : [Number(body.activeDays)];
  }

  const jobData: Job = {
    id: id || store.getNextJobId(),
    name: body.name.trim(),
    description: body.description || '',
    transferDirectionId: parseInt(body.transferDirectionId, 10) || 1,
    sourceEndpointId: srcId,
    destinationEndpointId: dstId,
    isEnabled: body.isEnabled === 'true' || body.isEnabled === true,
    isPaused: body.isPaused === 'true' || body.isPaused === true,
    scheduleTypeId: parseInt(body.scheduleTypeId, 10) || 1,
    cronExpression: body.cronExpression || undefined,
    intervalSeconds: body.intervalSeconds ? parseInt(body.intervalSeconds, 10) : undefined,
    timeZoneId: body.timeZoneId || 'Central Standard Time',
    activeFromTime: body.activeFromTime || undefined,
    activeToTime: body.activeToTime || undefined,
    activeDaysMask: 127,
    activeDays,
    stabilitySeconds: parseInt(body.stabilitySeconds, 10) || 30,
    maxParallelFiles: parseInt(body.maxParallelFiles, 10) || 4,
    maxRetries: parseInt(body.maxRetries, 10) || 3,
    retryBaseSeconds: parseInt(body.retryBaseSeconds, 10) || 30,
    duplicatePolicyId: parseInt(body.duplicatePolicyId, 10) || 1,
    encryptionProfileId: body.encryptionProfileId ? parseInt(body.encryptionProfileId, 10) : undefined,
    compressionOperationId: parseInt(body.compressionOperationId, 10) || 1,
    renamePattern: body.renamePattern || undefined,
    useTempNameOnUpload: body.useTempNameOnUpload === 'true' || body.useTempNameOnUpload === true,
    verifyAfterUpload: body.verifyAfterUpload === 'true' || body.verifyAfterUpload === true,
    virusScanEnabled: body.virusScanEnabled === 'true' || body.virusScanEnabled === true,
    generateChecksumManifest: body.generateChecksumManifest === 'true' || body.generateChecksumManifest === true,
    validateChecksumManifest: body.validateChecksumManifest === 'true' || body.validateChecksumManifest === true,
    useFileWatcher: body.useFileWatcher === 'true' || body.useFileWatcher === true,
    version: (parseInt(body.version, 10) || 1) + 1,
    rowVersion: '0x' + Math.random().toString(16).slice(2, 6),
    folderMaps: folderMaps.length > 0 ? folderMaps : [{ id: 1, sourcePath: '', destinationPath: '', recursive: false, preserveSubfolders: false }],
    filters,
    semaphore: {
      semaphoreModeId: parseInt(body['semaphore.semaphoreModeId'], 10) || 1,
      triggerPattern: body['semaphore.triggerPattern'] || '{filename}.done',
      deleteTriggerAfter: body['semaphore.deleteTriggerAfter'] === 'true' || body['semaphore.deleteTriggerAfter'] === true,
      transferTrigger: body['semaphore.transferTrigger'] === 'true' || body['semaphore.transferTrigger'] === true
    },
    postAction: {
      postActionTypeId: parseInt(body['postAction.postActionTypeId'], 10) || 1,
      archivePath: body['postAction.archivePath'] || 'archive',
      renamePattern: body['postAction.renamePattern'] || ''
    },
    notifications,
    sla: {
      id: 1,
      expectedByLocalTime: body['sla.expectedByLocalTime'] || '08:00',
      activeDays: [1, 2, 3, 4, 5],
      filePattern: body['sla.filePattern'] || '*',
      minFileCount: parseInt(body['sla.minFileCount'], 10) || 1,
      isEnabled: body['sla.isEnabled'] === 'true' || body['sla.isEnabled'] === true
    }
  };

  const requireApproval = store.globalSettings.get('Approval.RequireForJobChanges')?.settingValue === 'true';
  if (requireApproval && id !== 0) {
    store.changeRequests.unshift({
      id: Date.now(),
      entityName: 'Job',
      entityKey: String(id),
      operation: 'Modified',
      payloadJson: JSON.stringify(jobData, null, 2),
      requestedBy: res.locals.currentUser,
      requestedUtc: new Date().toISOString(),
      approvalStatusId: 1
    });
    setFlash(req, 'Job change submitted as Change Request for four-eyes approval.');
    return res.redirect('/Jobs');
  }

  if (id === 0) {
    store.jobs.push(jobData);
    store.addAudit('Job', String(jobData.id), 'Added', res.locals.currentUser);
    store.jobVersions.unshift({
      jobId: jobData.id,
      version: jobData.version,
      createdUtc: new Date().toISOString(),
      createdBy: res.locals.currentUser,
      snapshot: JSON.parse(JSON.stringify(jobData))
    });
    setFlash(req, `Job '${jobData.name}' created successfully.`);
  } else {
    const idx = store.jobs.findIndex(j => j.id === id);
    if (idx !== -1) {
      store.jobs[idx] = jobData;
      store.addAudit('Job', String(id), 'Modified', res.locals.currentUser);
      store.jobVersions.unshift({
        jobId: jobData.id,
        version: jobData.version,
        createdUtc: new Date().toISOString(),
        createdBy: res.locals.currentUser,
        snapshot: JSON.parse(JSON.stringify(jobData))
      });
      setFlash(req, `Job '${jobData.name}' saved (v${jobData.version}).`);
    }
  }

  res.redirect('/Jobs');
});

app.post('/Jobs/TogglePause/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const job = store.jobs.find(j => j.id === id);
  if (job) {
    job.isPaused = !job.isPaused;
    store.addAudit('Job', String(id), 'Modified', res.locals.currentUser);
    setFlash(req, `Job '${job.name}' is now ${job.isPaused ? 'paused' : 'resumed'}.`);
  }
  res.redirect('/Jobs');
});

app.post('/Jobs/DryRun/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const reqId = store.enqueueRequest({
    typeId: 2, // DryRun
    jobId: id,
    user: res.locals.currentUser
  });
  res.json({ requestId: reqId });
});

app.post('/Jobs/RunNow/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const reqId = store.enqueueRequest({
    typeId: 1, // RunNow
    jobId: id,
    user: res.locals.currentUser
  });
  res.json({ requestId: reqId });
});

app.get('/Jobs/Versions/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const job = store.jobs.find(j => j.id === id);
  const versions = store.jobVersions.filter(v => v.jobId === id);

  res.render('jobs/versions', {
    title: 'Job versions',
    activeNav: 'jobs',
    jobId: id,
    jobName: job?.name || 'Job #' + id,
    versions
  });
});

app.post('/Jobs/Rollback/:id/:version', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const version = parseInt(req.params.version, 10);
  const targetVer = store.jobVersions.find(v => v.jobId === id && v.version === version);
  const jobIdx = store.jobs.findIndex(j => j.id === id);

  if (targetVer && jobIdx !== -1) {
    const restored = JSON.parse(JSON.stringify(targetVer.snapshot));
    restored.version = store.jobs[jobIdx].version + 1;
    store.jobs[jobIdx] = restored;
    store.addAudit('Job', String(id), 'Modified', res.locals.currentUser);
    setFlash(req, `Rolled back to version ${version}.`);
  }
  res.redirect(`/Jobs/Edit/${id}`);
});

app.post('/Jobs/Clone/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const job = store.jobs.find(j => j.id === id);
  if (!job) return res.redirect('/Jobs');

  const cloned: Job = JSON.parse(JSON.stringify(job));
  cloned.id = store.getNextJobId();
  cloned.name = `${job.name} (Clone)`;
  cloned.isEnabled = false;
  cloned.version = 1;
  store.jobs.push(cloned);
  store.addAudit('Job', String(cloned.id), 'Added', res.locals.currentUser);
  setFlash(req, 'Job cloned (disabled by default). Review and enable it.');
  res.redirect(`/Jobs/Edit/${cloned.id}`);
});

app.post('/Jobs/Delete/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const hasHistory = store.histories.some(h => h.jobId === id);
  const idx = store.jobs.findIndex(j => j.id === id);

  if (idx !== -1) {
    if (hasHistory) {
      store.jobs[idx].isEnabled = false;
      store.addAudit('Job', String(id), 'Modified', res.locals.currentUser);
      setFlash(req, 'Job has run history, so it was disabled instead of deleted.');
    } else {
      store.jobs.splice(idx, 1);
      store.addAudit('Job', String(id), 'Deleted', res.locals.currentUser);
      setFlash(req, 'Job deleted.');
    }
  }
  res.redirect('/Jobs');
});

app.get('/Jobs/Export/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const job = store.jobs.find(j => j.id === id);
  if (!job) return res.status(404).send('Job not found');

  res.setHeader('Content-Type', 'application/json');
  res.setHeader('Content-Disposition', `attachment; filename="job-${id}.json"`);
  res.send(JSON.stringify(job, null, 2));
});

app.post('/Jobs/Import', upload.single('file') as any, (req: Request, res: Response) => {
  try {
    if (!req.file) throw new Error('No file uploaded.');
    const parsed = JSON.parse(req.file.buffer.toString('utf-8'));
    parsed.id = store.getNextJobId();
    parsed.name = `${parsed.name || 'Imported Job'} (Imported)`;
    parsed.isEnabled = false;
    parsed.version = 1;
    store.jobs.push(parsed);
    store.addAudit('Job', String(parsed.id), 'Added', res.locals.currentUser);
    setFlash(req, `Job '${parsed.name}' imported successfully (disabled by default).`);
  } catch (err: any) {
    setFlash(req, `Import failed: ${err.message}`);
  }
  res.redirect('/Jobs');
});

// -------------------------------------------------------------
// Endpoints Routes
// -------------------------------------------------------------
app.get('/Endpoints', (_req: Request, res: Response) => {
  res.render('endpoints/index', {
    title: 'Endpoints',
    activeNav: 'endpoints',
    endpoints: store.endpoints,
    endpointTypeNames: EndpointTypeNames
  });
});

app.get('/Endpoints/Create', (_req: Request, res: Response) => {
  res.render('endpoints/edit', {
    title: 'Add endpoint',
    activeNav: 'endpoints',
    model: {
      id: 0,
      name: '',
      endpointTypeId: 1,
      basePath: '',
      timeoutSeconds: 60,
      isEnabled: true
    }
  });
});

app.get('/Endpoints/Edit/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const ep = store.endpoints.find(e => e.id === id);
  if (!ep) return res.status(404).send('Endpoint not found');

  res.render('endpoints/edit', {
    title: 'Edit endpoint',
    activeNav: 'endpoints',
    model: ep
  });
});

app.post('/Endpoints/Save', (req: Request, res: Response) => {
  const body = req.body;
  const id = parseInt(body.id, 10) || 0;

  const endpointData = {
    id: id || store.getNextEndpointId(),
    name: body.name?.trim() || 'Endpoint',
    endpointTypeId: parseInt(body.endpointTypeId, 10) || 1,
    host: body.host || undefined,
    port: body.port ? parseInt(body.port, 10) : undefined,
    basePath: body.basePath?.trim() || '',
    hostKeyFingerprint: body.hostKeyFingerprint || undefined,
    baseUrl: body.baseUrl || undefined,
    listRoute: body.listRoute || undefined,
    downloadRoute: body.downloadRoute || undefined,
    uploadRoute: body.uploadRoute || undefined,
    deleteRoute: body.deleteRoute || undefined,
    renameRoute: body.renameRoute || undefined,
    timeoutSeconds: parseInt(body.timeoutSeconds, 10) || 60,
    isEnabled: body.isEnabled === 'true' || body.isEnabled === true,
    notes: body.notes || undefined,
    domain: body.domain || undefined,
    username: body.username || undefined,
    hasStoredCredential: Boolean(body.username || body.password || body.privateKey),
    rowVersion: '0x' + Math.random().toString(16).slice(2, 6)
  };

  if (id === 0) {
    store.endpoints.push(endpointData);
    store.addAudit('Endpoint', String(endpointData.id), 'Added', res.locals.currentUser);
    setFlash(req, `Endpoint '${endpointData.name}' created.`);
  } else {
    const idx = store.endpoints.findIndex(e => e.id === id);
    if (idx !== -1) {
      store.endpoints[idx] = endpointData;
      store.addAudit('Endpoint', String(id), 'Modified', res.locals.currentUser);
      setFlash(req, `Endpoint '${endpointData.name}' saved.`);
    }
  }

  res.redirect('/Endpoints');
});

app.post('/Endpoints/TestConnection/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const reqId = store.enqueueRequest({
    typeId: 3, // TestConnection
    endpointId: id,
    user: res.locals.currentUser
  });
  res.json({ requestId: reqId });
});

app.post('/Endpoints/Browse/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const pathParam = req.body.path || '';
  const reqId = store.enqueueRequest({
    typeId: 4, // Browse
    endpointId: id,
    path: pathParam,
    user: res.locals.currentUser
  });
  res.json({ requestId: reqId });
});

// -------------------------------------------------------------
// History Routes
// -------------------------------------------------------------
app.get('/History', (req: Request, res: Response) => {
  const jobId = req.query.jobId ? parseInt(req.query.jobId as string, 10) : null;
  const status = req.query.status as string || null;
  const page = parseInt(req.query.page as string, 10) || 1;

  let filtered = [...store.histories];
  if (jobId) filtered = filtered.filter(h => h.jobId === jobId);
  if (status) filtered = filtered.filter(h => h.transferStatusId === status);

  const total = filtered.length;
  const pageSize = 50;
  const paginated = filtered.slice((page - 1) * pageSize, page * pageSize);

  const items = paginated.map(h => ({
    ...h,
    jobName: store.jobs.find(j => j.id === h.jobId)?.name || 'Job #' + h.jobId
  }));

  res.render('history/index', {
    title: 'History',
    activeNav: 'history',
    histories: items,
    jobs: store.jobs,
    selectedJobId: jobId,
    selectedStatus: status,
    total,
    page
  });
});

app.get('/History/Details/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const history = store.histories.find(h => h.id === id);
  if (!history) return res.status(404).send('Transfer history record not found');

  const item = {
    ...history,
    jobName: store.jobs.find(j => j.id === history.jobId)?.name || 'Job #' + history.jobId
  };

  res.render('history/details', {
    title: 'Transfer details',
    activeNav: 'history',
    history: item
  });
});

app.post('/History/Reprocess/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const history = store.histories.find(h => h.id === id);
  if (history) {
    history.transferStatusId = 'Pending';
    history.attemptCount = 0;
    setFlash(req, 'File will be re-attempted on the next run (lease cleared).');
  }
  res.redirect(`/History/Details/${id}`);
});

app.get('/History/ExportCsv', (req: Request, res: Response) => {
  const jobId = req.query.jobId ? parseInt(req.query.jobId as string, 10) : null;
  const status = req.query.status as string || null;

  let filtered = [...store.histories];
  if (jobId) filtered = filtered.filter(h => h.jobId === jobId);
  if (status) filtered = filtered.filter(h => h.transferStatusId === status);

  let csv = 'Job,File,Status,SizeBytes,StartedUtc,DurationMs,Error\n';
  for (const h of filtered) {
    const job = store.jobs.find(j => j.id === h.jobId);
    const escapedErr = (h.errorMessage || '').replace(/"/g, '""');
    csv += `"${job?.name || ''}","${h.fileName}","${h.transferStatusId}",${h.sizeBytes},"${h.startedUtc}",${h.durationMs || ''},"${escapedErr}"\n`;
  }

  res.setHeader('Content-Type', 'text/csv');
  res.setHeader('Content-Disposition', 'attachment; filename="filebridge-history.csv"');
  res.send(csv);
});

// -------------------------------------------------------------
// Quarantine Routes
// -------------------------------------------------------------
app.get('/Quarantine', (_req: Request, res: Response) => {
  const held = store.quarantines
    .filter(q => q.quarantineStatusId === 1)
    .map(q => ({
      ...q,
      jobName: store.jobs.find(j => j.id === q.jobId)?.name || 'Job #' + q.jobId
    }));

  res.render('quarantine/index', {
    title: 'Quarantine',
    activeNav: 'quarantine',
    quarantines: held
  });
});

app.post('/Quarantine/Release/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const reqId = store.enqueueRequest({
    typeId: 5, // ReleaseQuarantine
    quarantineId: id,
    user: res.locals.currentUser
  });
  res.json({ requestId: reqId });
});

app.post('/Quarantine/Discard/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const reqId = store.enqueueRequest({
    typeId: 6, // DiscardQuarantine
    quarantineId: id,
    user: res.locals.currentUser
  });
  res.json({ requestId: reqId });
});

// -------------------------------------------------------------
// Approvals Routes
// -------------------------------------------------------------
app.get('/Approvals', (_req: Request, res: Response) => {
  const pending = store.changeRequests.filter(c => c.approvalStatusId === 1);
  res.render('approvals/index', {
    title: 'Approvals',
    activeNav: 'approvals',
    approvals: pending
  });
});

app.post('/Approvals/Approve/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const cr = store.changeRequests.find(c => c.id === id);
  if (cr) {
    if (cr.requestedBy === res.locals.currentUser) {
      setFlash(req, 'You cannot approve your own change request.');
      return res.redirect('/Approvals');
    }
    cr.approvalStatusId = 2; // Approved
    if (cr.entityName === 'Job' && cr.entityKey) {
      const jobId = parseInt(cr.entityKey, 10);
      const existingIdx = store.jobs.findIndex(j => j.id === jobId);
      if (existingIdx !== -1) {
        try {
          const payload = JSON.parse(cr.payloadJson);
          store.jobs[existingIdx] = { ...store.jobs[existingIdx], ...payload };
          store.addAudit('Job', String(jobId), 'Modified', res.locals.currentUser);
        } catch {}
      }
    }
    setFlash(req, 'Change approved and applied.');
  }
  res.redirect('/Approvals');
});

app.post('/Approvals/Reject/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const cr = store.changeRequests.find(c => c.id === id);
  if (cr) {
    cr.approvalStatusId = 3; // Rejected
    cr.reviewComment = req.body.comment || undefined;
    setFlash(req, 'Change rejected.');
  }
  res.redirect('/Approvals');
});

// -------------------------------------------------------------
// Encryption Profiles Routes
// -------------------------------------------------------------
app.get('/EncryptionProfiles', (req: Request, res: Response) => {
  const genKey = (req.session as any).generatedAesKey;
  (req.session as any).generatedAesKey = null;

  res.render('encryption/index', {
    title: 'Encryption profiles',
    activeNav: 'encryption',
    profiles: store.encryptionProfiles,
    encryptionOperationNames: EncryptionOperationNames,
    generatedKey: genKey
  });
});

app.get('/EncryptionProfiles/Create', (_req: Request, res: Response) => {
  res.render('encryption/edit', {
    title: 'Add encryption profile',
    activeNav: 'encryption',
    model: {
      id: 0,
      name: '',
      encryptionOperationId: 1,
      stripExtensionOnDecrypt: true,
      armorOutput: false,
      hasStoredKeys: false
    }
  });
});

app.get('/EncryptionProfiles/Edit/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const profile = store.encryptionProfiles.find(p => p.id === id);
  if (!profile) return res.status(404).send('Encryption profile not found');

  res.render('encryption/edit', {
    title: 'Edit encryption profile',
    activeNav: 'encryption',
    model: profile
  });
});

app.post('/EncryptionProfiles/Save', (req: Request, res: Response) => {
  const body = req.body;
  const id = parseInt(body.id, 10) || 0;
  const opId = parseInt(body.encryptionOperationId, 10) || 1;

  let generatedKey: string | null = null;
  let protectedAes = undefined;
  if ((opId === 6 || opId === 7) && !body.publicKey) {
    generatedKey = Buffer.from(Array.from({ length: 32 }, () => Math.floor(Math.random() * 256))).toString('hex');
    protectedAes = 'AES-256 GCM Key (Stored)';
  }

  const profileData = {
    id: id || store.encryptionProfiles.length + 1,
    name: body.name?.trim() || 'Profile',
    encryptionOperationId: opId,
    outputExtension: body.outputExtension || undefined,
    stripExtensionOnDecrypt: body.stripExtensionOnDecrypt === 'true' || body.stripExtensionOnDecrypt === true,
    armorOutput: body.armorOutput === 'true' || body.armorOutput === true,
    protectedPublicKey: body.publicKey ? 'PGP PUBLIC KEY (Stored)' : undefined,
    protectedPrivateKey: body.privateKey ? 'PGP PRIVATE KEY (Stored)' : undefined,
    protectedPassphrase: body.passphrase ? '••••••••' : undefined,
    protectedAesKey: protectedAes,
    hasStoredKeys: true,
    rowVersion: '0x' + Math.random().toString(16).slice(2, 6)
  };

  if (id === 0) {
    store.encryptionProfiles.push(profileData);
    store.addAudit('EncryptionProfile', String(profileData.id), 'Added', res.locals.currentUser);
    setFlash(req, `Encryption profile '${profileData.name}' saved.`);
  } else {
    const idx = store.encryptionProfiles.findIndex(p => p.id === id);
    if (idx !== -1) {
      store.encryptionProfiles[idx] = profileData;
      store.addAudit('EncryptionProfile', String(id), 'Modified', res.locals.currentUser);
      setFlash(req, `Encryption profile '${profileData.name}' saved.`);
    }
  }

  if (generatedKey) {
    (req.session as any).generatedAesKey = generatedKey;
  }

  res.redirect('/EncryptionProfiles');
});

// -------------------------------------------------------------
// Settings Routes
// -------------------------------------------------------------
app.get('/Settings', (_req: Request, res: Response) => {
  res.render('settings/index', {
    title: 'Settings',
    activeNav: 'settings',
    settings: Array.from(store.globalSettings.values()),
    roles: store.roleMappings,
    blackouts: store.blackoutWindows,
    notifications: store.globalNotifications
  });
});

app.post('/Settings/SaveSetting', (req: Request, res: Response) => {
  const { key, value } = req.body;
  const s = store.globalSettings.get(key);
  if (s) {
    s.settingValue = value;
    store.addAudit('GlobalSetting', key, 'Modified', res.locals.currentUser);
    setFlash(req, `Setting '${key}' saved.`);
  }
  res.redirect('/Settings');
});

app.post('/Settings/AddRoleMapping', (req: Request, res: Response) => {
  const { adGroup, role } = req.body;
  if (adGroup && role) {
    store.roleMappings.push({
      id: Date.now(),
      adGroup: adGroup.trim(),
      appRoleId: role
    });
    store.addAudit('RoleMapping', adGroup, 'Added', res.locals.currentUser);
    setFlash(req, `Role mapping added for '${adGroup}'.`);
  }
  res.redirect('/Settings');
});

app.post('/Settings/RemoveRoleMapping/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const idx = store.roleMappings.findIndex(r => r.id === id);
  if (idx !== -1) {
    const removed = store.roleMappings.splice(idx, 1)[0];
    store.addAudit('RoleMapping', removed.adGroup, 'Deleted', res.locals.currentUser);
    setFlash(req, `Role mapping for '${removed.adGroup}' removed.`);
  }
  res.redirect('/Settings');
});

app.post('/Settings/AddBlackout', (req: Request, res: Response) => {
  const { startUtc, endUtc, reason } = req.body;
  if (startUtc && endUtc) {
    store.blackoutWindows.push({
      id: Date.now(),
      jobId: null,
      startUtc: new Date(startUtc).toISOString(),
      endUtc: new Date(endUtc).toISOString(),
      reason: reason || undefined
    });
    store.addAudit('BlackoutWindow', 'Global', 'Added', res.locals.currentUser);
    setFlash(req, 'Global blackout window scheduled.');
  }
  res.redirect('/Settings');
});

app.post('/Settings/AddGlobalNotification', (req: Request, res: Response) => {
  const { evt, channel, target } = req.body;
  if (evt && channel && target) {
    store.globalNotifications.push({
      id: Date.now(),
      jobId: null,
      notificationEventId: evt,
      notificationChannelId: channel,
      target: target.trim(),
      isEnabled: true
    });
    store.addAudit('NotificationRule', target, 'Added', res.locals.currentUser);
    setFlash(req, 'Global notification rule added.');
  }
  res.redirect('/Settings');
});

// -------------------------------------------------------------
// Audit Log Routes
// -------------------------------------------------------------
app.get('/Audit', (req: Request, res: Response) => {
  const entity = req.query.entity as string || '';
  const page = parseInt(req.query.page as string, 10) || 1;

  let filtered = [...store.configAudits];
  if (entity) {
    filtered = filtered.filter(a => a.entityName === entity);
  }

  const entities = Array.from(new Set(store.configAudits.map(a => a.entityName))).sort();
  const total = filtered.length;
  const pageSize = 50;
  const paginated = filtered.slice((page - 1) * pageSize, page * pageSize);

  res.render('audit/index', {
    title: 'Audit log',
    activeNav: 'audit',
    audits: paginated,
    entities,
    selectedEntity: entity,
    total,
    page
  });
});

// -------------------------------------------------------------
// REST API v1 (polled by site.js)
// -------------------------------------------------------------
app.get('/api/v1/requests/:id', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const r = store.runRequests.get(id);
  if (!r) return res.status(404).json({ error: 'Request not found' });

  res.json({
    id: r.id,
    status: r.requestStatusId,
    result: r.resultJson,
    resultJson: r.resultJson,
    completedUtc: r.completedUtc
  });
});

app.get('/api/v1/jobs', (_req: Request, res: Response) => {
  res.json(
    store.jobs.map(j => ({
      id: j.id,
      name: j.name,
      isEnabled: j.isEnabled,
      isPaused: j.isPaused
    }))
  );
});

app.post('/api/v1/jobs/:id/run', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const reqId = store.enqueueRequest({
    typeId: 1, // RunNow
    jobId: id,
    user: res.locals.currentUser
  });
  res.json({ requestId: reqId });
});

app.get('/api/v1/jobs/:id/status', (req: Request, res: Response) => {
  const id = parseInt(req.params.id, 10);
  const last = store.histories
    .filter(h => h.jobId === id)
    .sort((a, b) => new Date(b.startedUtc).getTime() - new Date(a.startedUtc).getTime())[0];
  if (!last) return res.json(null);
  res.json({ startedUtc: last.startedUtc, status: last.transferStatusId });
});

// -------------------------------------------------------------
// Start server on port 3000, 0.0.0.0
// -------------------------------------------------------------
const PORT = 3000;
app.listen(PORT, '0.0.0.0', () => {
  console.log(`FileBridge server listening on http://0.0.0.0:${PORT}`);
});
