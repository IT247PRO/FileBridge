// In-memory data store for FileBridge (migrated to Node.js / Express)

export interface Endpoint {
  id: number;
  name: string;
  endpointTypeId: number; // 1: Smb, 2: Sftp, 3: Https, 4: LocalDisk
  host?: string;
  port?: number;
  basePath?: string;
  hostKeyFingerprint?: string;
  baseUrl?: string;
  listRoute?: string;
  downloadRoute?: string;
  uploadRoute?: string;
  deleteRoute?: string;
  renameRoute?: string;
  timeoutSeconds: number;
  isEnabled: boolean;
  notes?: string;
  domain?: string;
  username?: string;
  hasStoredCredential?: boolean;
  rowVersion?: string;
}

export interface FolderMap {
  id: number;
  sourcePath: string;
  destinationPath: string;
  recursive: boolean;
  preserveSubfolders: boolean;
}

export interface FilterModel {
  id: number;
  pattern: string;
  isRegex: boolean;
  isExclude: boolean;
  minSizeBytes?: number;
  maxSizeBytes?: number;
  minAgeSeconds?: number;
  allowedFileTypes?: string;
}

export interface SemaphoreModel {
  semaphoreModeId: number; // 1: None, 2: PerFile, 3: Batch
  triggerPattern: string;
  deleteTriggerAfter: boolean;
  transferTrigger: boolean;
}

export interface PostActionModel {
  postActionTypeId: number; // 1: None, 2: Delete, 3: Archive, 4: Rename
  archivePath?: string;
  renamePattern?: string;
}

export interface NotificationRule {
  id: number;
  jobId?: number | null;
  notificationEventId: string;
  notificationChannelId: string;
  target: string;
  isEnabled: boolean;
}

export interface SlaModel {
  id: number;
  expectedByLocalTime: string;
  activeDays: number[];
  filePattern?: string;
  minFileCount: number;
  isEnabled: boolean;
}

export interface Job {
  id: number;
  name: string;
  description?: string;
  transferDirectionId: number; // 1: Inbound, 2: Outbound
  sourceEndpointId: number;
  destinationEndpointId: number;
  isEnabled: boolean;
  isPaused: boolean;
  scheduleTypeId: number; // 1: Cron, 2: Interval
  cronExpression?: string;
  intervalSeconds?: number;
  timeZoneId: string;
  activeFromTime?: string;
  activeToTime?: string;
  activeDaysMask: number;
  activeDays?: number[];
  stabilitySeconds: number;
  maxParallelFiles: number;
  maxRetries: number;
  retryBaseSeconds: number;
  duplicatePolicyId: number; // 1: Skip, 2: Overwrite, 3: Version, 4: Fail
  encryptionProfileId?: number;
  compressionOperationId: number; // 1: None, 2: Zip, 3: Unzip
  renamePattern?: string;
  useTempNameOnUpload: boolean;
  verifyAfterUpload: boolean;
  virusScanEnabled: boolean;
  generateChecksumManifest: boolean;
  validateChecksumManifest: boolean;
  useFileWatcher: boolean;
  version: number;
  rowVersion?: string;
  folderMaps: FolderMap[];
  filters: FilterModel[];
  semaphore: SemaphoreModel;
  postAction: PostActionModel;
  notifications: NotificationRule[];
  sla?: SlaModel;
}

export interface JobVersion {
  jobId: number;
  version: number;
  createdUtc: string;
  createdBy: string;
  snapshot: Job;
}

export interface TransferHistory {
  id: number;
  jobId: number;
  fileName: string;
  sourcePath: string;
  destinationPath: string;
  transferStatusId: string; // 'Pending' | 'InProgress' | 'Succeeded' | 'Failed' | 'Skipped' | 'Quarantined' | 'DryRun'
  sizeBytes: number;
  startedUtc: string;
  completedUtc?: string;
  durationMs?: number;
  attemptCount: number;
  nodeName: string;
  triggeredBy: string;
  errorMessage?: string;
}

export interface QuarantineItem {
  id: number;
  jobId: number;
  originalPath: string;
  heldPath: string;
  reason: string;
  sizeBytes: number;
  createdUtc: string;
  quarantineStatusId: number; // 1: Held, 2: Released, 3: Discarded
}

export interface ChangeRequest {
  id: number;
  entityName: string;
  entityKey?: string;
  operation: string;
  payloadJson: string;
  requestedBy: string;
  requestedUtc: string;
  approvalStatusId: number; // 1: Pending, 2: Approved, 3: Rejected
  reviewComment?: string;
}

export interface EncryptionProfile {
  id: number;
  name: string;
  encryptionOperationId: number; // 1: None, 2: PgpEncrypt, 3: PgpDecrypt, 4: PgpEncryptAndSign, 5: PgpDecryptAndVerify, 6: AesEncrypt, 7: AesDecrypt
  outputExtension?: string;
  stripExtensionOnDecrypt: boolean;
  armorOutput: boolean;
  protectedPublicKey?: string;
  protectedPrivateKey?: string;
  protectedPassphrase?: string;
  protectedAesKey?: string;
  hasStoredKeys?: boolean;
  rowVersion?: string;
}

export interface GlobalSetting {
  settingKey: string;
  settingValue: string;
  description: string;
}

export interface RoleMapping {
  id: number;
  adGroup: string;
  appRoleId: string;
}

export interface BlackoutWindow {
  id: number;
  jobId?: number | null;
  startUtc: string;
  endUtc: string;
  reason?: string;
}

export interface NodeHeartbeat {
  nodeName: string;
  nodeRole: string;
  lastSeenUtc: string;
}

export interface ConfigAudit {
  id: number;
  entityName: string;
  entityKey: string;
  auditActionId: string; // 'Added' | 'Modified' | 'Deleted'
  changedBy: string;
  changedUtc: string;
}

export interface RunRequest {
  id: number;
  requestTypeId: number;
  jobId?: number | null;
  endpointId?: number | null;
  quarantineId?: number | null;
  path?: string | null;
  requestStatusId: string; // 'Queued' | 'Running' | 'Completed' | 'Failed'
  requestedBy: string;
  requestedUtc: string;
  completedUtc?: string;
  resultJson?: string;
}

export const EndpointTypeNames: Record<number, string> = {
  1: 'Smb',
  2: 'Sftp',
  3: 'Https',
  4: 'LocalDisk'
};

export const TransferDirectionNames: Record<number, string> = {
  1: 'Inbound',
  2: 'Outbound'
};

export const EncryptionOperationNames: Record<number, string> = {
  1: 'None',
  2: 'PgpEncrypt',
  3: 'PgpDecrypt',
  4: 'PgpEncryptAndSign',
  5: 'PgpDecryptAndVerify',
  6: 'AesEncrypt',
  7: 'AesDecrypt'
};

export const ScheduleTypeNames: Record<number, string> = {
  1: 'Cron',
  2: 'Interval'
};

export const DuplicatePolicyNames: Record<number, string> = {
  1: 'Skip',
  2: 'Overwrite',
  3: 'Version',
  4: 'Fail'
};

export const CompressionOperationNames: Record<number, string> = {
  1: 'None',
  2: 'Zip',
  3: 'Unzip'
};

export const SemaphoreModeNames: Record<number, string> = {
  1: 'None',
  2: 'PerFile',
  3: 'Batch'
};

export const PostActionTypeNames: Record<number, string> = {
  1: 'None',
  2: 'Delete',
  3: 'Archive',
  4: 'Rename'
};

class FileBridgeStore {
  endpoints: Endpoint[] = [
    {
      id: 1,
      name: 'Exchangelka Share',
      endpointTypeId: 1,
      basePath: '\\\\exchangelka\\partner_drop\\inbound',
      timeoutSeconds: 60,
      isEnabled: true,
      notes: 'External partner exchange UNC share read by gMSA',
      hasStoredCredential: false,
      rowVersion: '0x01'
    },
    {
      id: 2,
      name: 'Internal SMB File Server',
      endpointTypeId: 1,
      basePath: '\\\\fileserver.corp\\finance\\incoming',
      timeoutSeconds: 60,
      isEnabled: true,
      notes: 'Main internal SMB corporate repository',
      hasStoredCredential: true,
      domain: 'CORP',
      username: 'svc_fb_ingest',
      rowVersion: '0x02'
    },
    {
      id: 3,
      name: 'Partner SFTP Gateway',
      endpointTypeId: 2,
      host: 'sftp.partner.example.com',
      port: 22,
      basePath: '/outbound/reports',
      hostKeyFingerprint: '4t7fXy19Q2sW83hB2L3m4p7r8s9t0v',
      timeoutSeconds: 120,
      isEnabled: true,
      notes: 'SFTP drop endpoint for daily partner settlement batches',
      hasStoredCredential: true,
      username: 'agency_transfer_user',
      rowVersion: '0x03'
    },
    {
      id: 4,
      name: 'Regulatory REST API',
      endpointTypeId: 3,
      baseUrl: 'https://api.regulatory.example.gov/v2',
      listRoute: '/transfers',
      downloadRoute: '/transfers/{id}/download',
      uploadRoute: '/transfers/upload',
      deleteRoute: '/transfers/{id}',
      timeoutSeconds: 90,
      isEnabled: true,
      notes: 'HTTPS JSON/REST automated filing API',
      hasStoredCredential: false,
      rowVersion: '0x04'
    }
  ];

  jobs: Job[] = [
    {
      id: 1,
      name: 'Exchangelka to Internal Ingest',
      description: 'Ingests transaction and settlement manifests from Exchangelka share into internal corporate storage',
      transferDirectionId: 1,
      sourceEndpointId: 1,
      destinationEndpointId: 2,
      isEnabled: true,
      isPaused: false,
      scheduleTypeId: 1,
      cronExpression: '0 0/5 * * * ?',
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
      renamePattern: '{YYYY}{MM}{DD}_{filename}',
      useTempNameOnUpload: true,
      verifyAfterUpload: true,
      virusScanEnabled: true,
      generateChecksumManifest: true,
      validateChecksumManifest: false,
      useFileWatcher: true,
      version: 1,
      rowVersion: '0x01',
      folderMaps: [
        {
          id: 1,
          sourcePath: 'incoming/daily',
          destinationPath: 'archive/daily',
          recursive: true,
          preserveSubfolders: true
        }
      ],
      filters: [
        {
          id: 1,
          pattern: '*.csv;*.xml',
          isRegex: false,
          isExclude: false,
          minSizeBytes: 100,
          minAgeSeconds: 15,
          allowedFileTypes: 'csv,xml'
        }
      ],
      semaphore: {
        semaphoreModeId: 2,
        triggerPattern: '{filename}.done',
        deleteTriggerAfter: true,
        transferTrigger: false
      },
      postAction: {
        postActionTypeId: 3,
        archivePath: 'processed',
        renamePattern: ''
      },
      notifications: [
        {
          id: 1,
          jobId: 1,
          notificationEventId: 'JobFailed',
          notificationChannelId: 'Email',
          target: 'ops-alerts@agency.gov',
          isEnabled: true
        }
      ],
      sla: {
        id: 1,
        expectedByLocalTime: '08:00',
        activeDays: [1, 2, 3, 4, 5],
        filePattern: '*.csv',
        minFileCount: 1,
        isEnabled: true
      }
    },
    {
      id: 2,
      name: 'Daily Settlement Export to SFTP',
      description: 'Pushes signed and encrypted settlement archives to Partner SFTP gateway',
      transferDirectionId: 2,
      sourceEndpointId: 2,
      destinationEndpointId: 3,
      isEnabled: true,
      isPaused: false,
      scheduleTypeId: 2,
      intervalSeconds: 600,
      timeZoneId: 'Eastern Standard Time',
      activeFromTime: '06:00',
      activeToTime: '22:00',
      activeDaysMask: 127,
      activeDays: [1, 2, 3, 4, 5],
      stabilitySeconds: 45,
      maxParallelFiles: 2,
      maxRetries: 5,
      retryBaseSeconds: 60,
      duplicatePolicyId: 1,
      encryptionProfileId: 1,
      compressionOperationId: 2,
      renamePattern: '',
      useTempNameOnUpload: true,
      verifyAfterUpload: true,
      virusScanEnabled: true,
      generateChecksumManifest: false,
      validateChecksumManifest: false,
      useFileWatcher: false,
      version: 1,
      rowVersion: '0x02',
      folderMaps: [
        {
          id: 2,
          sourcePath: 'outbound/settlements',
          destinationPath: '/outbound/reports/daily',
          recursive: false,
          preserveSubfolders: false
        }
      ],
      filters: [
        {
          id: 2,
          pattern: '*_settlement.zip',
          isRegex: false,
          isExclude: false
        }
      ],
      semaphore: {
        semaphoreModeId: 1,
        triggerPattern: '',
        deleteTriggerAfter: false,
        transferTrigger: false
      },
      postAction: {
        postActionTypeId: 2,
        archivePath: '',
        renamePattern: ''
      },
      notifications: [
        {
          id: 2,
          jobId: 2,
          notificationEventId: 'JobFailed',
          notificationChannelId: 'Teams',
          target: 'https://teams.office.com/webhook/settlements',
          isEnabled: true
        }
      ]
    }
  ];

  jobVersions: JobVersion[] = [
    {
      jobId: 1,
      version: 1,
      createdUtc: new Date(Date.now() - 86400000 * 2).toISOString(),
      createdBy: 'AGENCY\\admin',
      snapshot: null as any
    },
    {
      jobId: 2,
      version: 1,
      createdUtc: new Date(Date.now() - 86400000).toISOString(),
      createdBy: 'AGENCY\\admin',
      snapshot: null as any
    }
  ];

  histories: TransferHistory[] = [
    {
      id: 101,
      jobId: 1,
      fileName: 'trans_20260923_001.csv',
      sourcePath: '\\\\exchangelka\\partner_drop\\inbound\\incoming\\daily\\trans_20260923_001.csv',
      destinationPath: '\\\\fileserver.corp\\finance\\incoming\\archive\\daily\\20260923_trans_20260923_001.csv',
      transferStatusId: 'Succeeded',
      sizeBytes: 4194304,
      startedUtc: new Date(Date.now() - 14400000).toISOString(),
      completedUtc: new Date(Date.now() - 14390000).toISOString(),
      durationMs: 10240,
      attemptCount: 1,
      nodeName: 'FB-WORKER-01',
      triggeredBy: 'Schedule (Interval)',
      errorMessage: undefined
    },
    {
      id: 102,
      jobId: 1,
      fileName: 'trans_20260923_002.csv',
      sourcePath: '\\\\exchangelka\\partner_drop\\inbound\\incoming\\daily\\trans_20260923_002.csv',
      destinationPath: '\\\\fileserver.corp\\finance\\incoming\\archive\\daily\\20260923_trans_20260923_002.csv',
      transferStatusId: 'Succeeded',
      sizeBytes: 8388608,
      startedUtc: new Date(Date.now() - 7200000).toISOString(),
      completedUtc: new Date(Date.now() - 7182000).toISOString(),
      durationMs: 18450,
      attemptCount: 1,
      nodeName: 'FB-WORKER-02',
      triggeredBy: 'FileWatcher',
      errorMessage: undefined
    },
    {
      id: 103,
      jobId: 1,
      fileName: 'trans_corrupt_header.csv',
      sourcePath: '\\\\exchangelka\\partner_drop\\inbound\\incoming\\daily\\trans_corrupt_header.csv',
      destinationPath: '\\\\fileserver.corp\\finance\\incoming\\archive\\daily\\trans_corrupt_header.csv',
      transferStatusId: 'Failed',
      sizeBytes: 524288,
      startedUtc: new Date(Date.now() - 3600000).toISOString(),
      completedUtc: new Date(Date.now() - 3595000).toISOString(),
      durationMs: 5120,
      attemptCount: 3,
      nodeName: 'FB-WORKER-01',
      triggeredBy: 'Schedule (Interval)',
      errorMessage: 'Validation failure: Checksum mismatch. Expected SHA256 "a1b2c3" but calculated "d4e5f6".'
    },
    {
      id: 104,
      jobId: 2,
      fileName: 'settlement_batch_99.zip',
      sourcePath: '\\\\fileserver.corp\\finance\\incoming\\outbound\\settlements\\settlement_batch_99.zip',
      destinationPath: '/outbound/reports/daily/settlement_batch_99.zip',
      transferStatusId: 'Succeeded',
      sizeBytes: 15728640,
      startedUtc: new Date(Date.now() - 1800000).toISOString(),
      completedUtc: new Date(Date.now() - 1770000).toISOString(),
      durationMs: 30100,
      attemptCount: 1,
      nodeName: 'FB-WORKER-01',
      triggeredBy: 'Manual (RunNow)',
      errorMessage: undefined
    }
  ];

  quarantines: QuarantineItem[] = [
    {
      id: 1,
      jobId: 1,
      originalPath: '\\\\exchangelka\\partner_drop\\inbound\\incoming\\daily\\malformed_payload.bin',
      heldPath: '\\\\fileserver.corp\\quarantine\\malformed_payload.bin.quar',
      reason: 'FileTypeSniffer: Extension .bin not in allowed MIME list [text/csv, application/xml].',
      sizeBytes: 2048,
      createdUtc: new Date(Date.now() - 18000000).toISOString(),
      quarantineStatusId: 1
    }
  ];

  changeRequests: ChangeRequest[] = [
    {
      id: 1,
      entityName: 'Job',
      entityKey: '2',
      operation: 'Modified',
      payloadJson: JSON.stringify(
        {
          name: 'Daily Settlement Export to SFTP',
          maxRetries: 5,
          retryBaseSeconds: 60,
          notes: 'Updated retry policy to accommodate partner SFTP maintenance windows'
        },
        null,
        2
      ),
      requestedBy: 'CORP\\jsmith',
      requestedUtc: new Date(Date.now() - 7200000).toISOString(),
      approvalStatusId: 1
    }
  ];

  encryptionProfiles: EncryptionProfile[] = [
    {
      id: 1,
      name: 'Partner PGP Keyring',
      encryptionOperationId: 4, // PgpEncryptAndSign
      outputExtension: '.pgp',
      stripExtensionOnDecrypt: true,
      armorOutput: true,
      protectedPublicKey: 'PGP PUBLIC KEY BLOCK (Stored)',
      protectedPrivateKey: 'PGP PRIVATE KEY BLOCK (Stored)',
      protectedPassphrase: '••••••••',
      hasStoredKeys: true,
      rowVersion: '0x01'
    },
    {
      id: 2,
      name: 'Internal AES-256 Storage',
      encryptionOperationId: 6, // AesEncrypt
      outputExtension: '.enc',
      stripExtensionOnDecrypt: true,
      armorOutput: false,
      protectedAesKey: 'AES-256 GCM Key (Stored)',
      hasStoredKeys: true,
      rowVersion: '0x02'
    }
  ];

  globalSettings: Map<string, GlobalSetting> = new Map([
    ['Engine.KillSwitch', { settingKey: 'Engine.KillSwitch', settingValue: 'false', description: 'Stop all scheduled transfers immediately (emergency stop).' }],
    ['Approval.RequireForJobChanges', { settingKey: 'Approval.RequireForJobChanges', settingValue: 'false', description: 'Require a second person to approve job changes (four-eyes).' }],
    ['Retention.HistoryDays', { settingKey: 'Retention.HistoryDays', settingValue: '400', description: 'Days of transfer history to keep.' }],
    ['Retention.LeaseDays', { settingKey: 'Retention.LeaseDays', settingValue: '90', description: 'Days to remember completed files (older unchanged files could be re-sent if still at the source).' }],
    ['Retention.RequestDays', { settingKey: 'Retention.RequestDays', settingValue: '30', description: 'Days to keep test, browse and run-now requests.' }],
    ['Retention.AuditDays', { settingKey: 'Retention.AuditDays', settingValue: '2555', description: 'Days of configuration audit to keep (7 years).' }],
    ['Retention.LogDays', { settingKey: 'Retention.LogDays', settingValue: '90', description: 'Days of application log rows to keep in SQL.' }],
    ['Retention.QuarantineDays', { settingKey: 'Retention.QuarantineDays', settingValue: '180', description: 'Days to keep reviewed quarantine records and files.' }]
  ]);

  roleMappings: RoleMapping[] = [
    { id: 1, adGroup: 'AGENCY\\FileBridge-Admins', appRoleId: 'Admin' },
    { id: 2, adGroup: 'AGENCY\\FileBridge-Operators', appRoleId: 'Operator' },
    { id: 3, adGroup: 'AGENCY\\FileBridge-Viewers', appRoleId: 'Viewer' }
  ];

  blackoutWindows: BlackoutWindow[] = [
    {
      id: 1,
      jobId: null,
      startUtc: new Date(Date.now() + 86400000).toISOString(),
      endUtc: new Date(Date.now() + 97200000).toISOString(),
      reason: 'SAN Storage Array Firmware Upgrade'
    }
  ];

  globalNotifications: NotificationRule[] = [
    {
      id: 1,
      jobId: null,
      notificationEventId: 'JobFailed',
      notificationChannelId: 'Email',
      target: 'filebridge-ops@agency.gov',
      isEnabled: true
    },
    {
      id: 2,
      jobId: null,
      notificationEventId: 'FileQuarantined',
      notificationChannelId: 'Email',
      target: 'security-soc@agency.gov',
      isEnabled: true
    }
  ];

  nodeHeartbeats: NodeHeartbeat[] = [
    { nodeName: 'FB-WORKER-01', nodeRole: 'Worker', lastSeenUtc: new Date().toISOString() },
    { nodeName: 'FB-WORKER-02', nodeRole: 'Worker', lastSeenUtc: new Date(Date.now() - 30000).toISOString() }
  ];

  configAudits: ConfigAudit[] = [
    {
      id: 1,
      entityName: 'Job',
      entityKey: '1',
      auditActionId: 'Modified',
      changedBy: 'AGENCY\\admin',
      changedUtc: new Date(Date.now() - 86400000).toISOString()
    },
    {
      id: 2,
      entityName: 'Endpoint',
      entityKey: '1',
      auditActionId: 'Added',
      changedBy: 'AGENCY\\admin',
      changedUtc: new Date(Date.now() - 172800000).toISOString()
    },
    {
      id: 3,
      entityName: 'EncryptionProfile',
      entityKey: '1',
      auditActionId: 'Added',
      changedBy: 'AGENCY\\admin',
      changedUtc: new Date(Date.now() - 259200000).toISOString()
    }
  ];

  runRequests: Map<number, RunRequest> = new Map();
  private nextRequestId = 1001;
  private nextAuditId = 10;
  private nextEndpointId = 10;
  private nextJobId = 10;

  constructor() {
    this.jobVersions[0].snapshot = JSON.parse(JSON.stringify(this.jobs[0]));
    this.jobVersions[1].snapshot = JSON.parse(JSON.stringify(this.jobs[1]));
  }

  enqueueRequest(params: {
    typeId: number;
    jobId?: number | null;
    endpointId?: number | null;
    quarantineId?: number | null;
    path?: string | null;
    user?: string;
  }): number {
    const id = this.nextRequestId++;
    const now = new Date().toISOString();
    
    // Create immediate result based on request type
    let resultObj: any = { message: 'Action queued.' };
    if (params.typeId === 3) { // TestConnection
      const ep = this.endpoints.find(e => e.id === params.endpointId);
      const isSftp = ep?.endpointTypeId === 2;
      resultObj = {
        success: true,
        message: `Connected successfully to ${ep?.name || 'endpoint'}. Read/write lease check verified.`,
        observedHostKey: isSftp ? (ep?.hostKeyFingerprint || '4t7fXy19Q2sW83hB2L3m4p7r8s9t0v') : null
      };
    } else if (params.typeId === 4) { // Browse
      const ep = this.endpoints.find(e => e.id === params.endpointId);
      const curPath = params.path || '';
      resultObj = {
        path: curPath,
        parent: curPath.includes('/') ? curPath.substring(0, curPath.lastIndexOf('/')) : '',
        folders: curPath ? [`${curPath}/archive`, `${curPath}/incoming`] : ['incoming', 'outgoing', 'archive', 'staging'],
        files: [
          { name: 'settlement_20260923.csv', size: 4291824, lastModifiedUtc: new Date().toISOString() },
          { name: 'manifest_data.xml', size: 1048576, lastModifiedUtc: new Date(Date.now() - 3600000).toISOString() },
          { name: 'audit_log_sample.json', size: 52428, lastModifiedUtc: new Date(Date.now() - 7200000).toISOString() }
        ]
      };
    } else if (params.typeId === 2) { // DryRun
      const job = this.jobs.find(j => j.id === params.jobId);
      resultObj = {
        planned: [
          {
            sourcePath: `${job?.folderMaps?.[0]?.sourcePath || 'incoming'}/batch_sample_01.csv`,
            size: 2097152,
            destinationPath: `${job?.folderMaps?.[0]?.destinationPath || 'archive'}/20260923_batch_sample_01.csv`,
            note: 'Passes all filter and stability rules'
          },
          {
            sourcePath: `${job?.folderMaps?.[0]?.sourcePath || 'incoming'}/batch_sample_02.csv`,
            size: 1548291,
            destinationPath: `${job?.folderMaps?.[0]?.destinationPath || 'archive'}/20260923_batch_sample_02.csv`,
            note: 'Passes all filter and stability rules'
          }
        ]
      };
    } else if (params.typeId === 1) { // RunNow
      const job = this.jobs.find(j => j.id === params.jobId);
      const newHist: TransferHistory = {
        id: Date.now(),
        jobId: job?.id || 1,
        fileName: `manual_trigger_${Date.now().toString().slice(-4)}.csv`,
        sourcePath: `\\\\exchangelka\\partner_drop\\inbound\\incoming\\daily\\manual_trigger_${Date.now().toString().slice(-4)}.csv`,
        destinationPath: `\\\\fileserver.corp\\finance\\incoming\\archive\\daily\\manual_trigger_${Date.now().toString().slice(-4)}.csv`,
        transferStatusId: 'Succeeded',
        sizeBytes: 3145728,
        startedUtc: new Date().toISOString(),
        completedUtc: new Date(Date.now() + 2000).toISOString(),
        durationMs: 2340,
        attemptCount: 1,
        nodeName: 'FB-WORKER-01',
        triggeredBy: 'Manual (RunNow)',
        errorMessage: undefined
      };
      this.histories.unshift(newHist);
      resultObj = {
        message: `RunNow triggered on FB-WORKER-01. Successfully processed 1 file (3.0 MB).`
      };
    } else if (params.typeId === 5) { // ReleaseQuarantine
      const q = this.quarantines.find(item => item.id === params.quarantineId);
      if (q) {
        q.quarantineStatusId = 2; // Released
        resultObj = { message: 'File released from quarantine and enqueued for re-processing pipeline.' };
      }
    } else if (params.typeId === 6) { // DiscardQuarantine
      const q = this.quarantines.find(item => item.id === params.quarantineId);
      if (q) {
        q.quarantineStatusId = 3; // Discarded
        resultObj = { message: 'Quarantined file successfully discarded and permanently removed.' };
      }
    }

    const req: RunRequest = {
      id,
      requestTypeId: params.typeId,
      jobId: params.jobId,
      endpointId: params.endpointId,
      quarantineId: params.quarantineId,
      path: params.path,
      requestStatusId: 'Completed',
      requestedBy: params.user || 'AGENCY\\admin',
      requestedUtc: now,
      completedUtc: now,
      resultJson: JSON.stringify(resultObj)
    };

    this.runRequests.set(id, req);
    return id;
  }

  addAudit(entityName: string, entityKey: string, action: 'Added' | 'Modified' | 'Deleted', changedBy: string = 'AGENCY\\admin') {
    this.configAudits.unshift({
      id: this.nextAuditId++,
      entityName,
      entityKey,
      auditActionId: action,
      changedBy,
      changedUtc: new Date().toISOString()
    });
  }

  getKillSwitch(): boolean {
    return this.globalSettings.get('Engine.KillSwitch')?.settingValue === 'true';
  }

  setKillSwitch(val: boolean) {
    const s = this.globalSettings.get('Engine.KillSwitch');
    if (s) {
      s.settingValue = String(val);
      this.addAudit('GlobalSetting', 'Engine.KillSwitch', 'Modified');
    }
  }

  getNextEndpointId(): number {
    return this.nextEndpointId++;
  }

  getNextJobId(): number {
    return this.nextJobId++;
  }
}

export const store = new FileBridgeStore();
