# FileBridge

An on-premises .NET 8 service that moves files between endpoints — network (UNC/SMB) shares, SFTP, and HTTPS
REST APIs — with a SQL Server backend, a Quartz-clustered Worker engine, and a load-balanced ASP.NET Core
MVC admin site. In this deployment both sides are UNC network shares: "Exchangelka" and the internal SMB
file server are each configured as an `Endpoint` with type `Smb`, so both are read and written the same way,
over `\\server\share\...` paths, using the Worker's gMSA identity (or a stored credential) — no SFTP/HTTPS
involved for either side.

> **This code has not been compiled.** The sandbox this was built in has no .NET SDK and no outbound access
> to NuGet (`api.nuget.org` returns 403 through the egress proxy), so `dotnet restore`/`build` could not be
> run here. Everything below is written to compile against .NET 8 / EF Core 8 / Quartz 3.13, but treat it as
> a thorough first draft: build it, fix whatever the compiler finds (there will likely be a handful of small
> issues — a using directive, a nullable warning treated as an error, etc.), and run the test project before
> trusting it with real files. The Quartz SQL script (`db/05_quartz.sql`) was pulled directly from the
> official quartznet/quartznet repository and is unmodified apart from the table-prefix substitution, so
> that part is much more trustworthy than the hand-written C#.

## Architecture

```
FileBridge.Core            Domain model, enums, abstractions (IFileEndpoint), business rules — no I/O, no EF.
FileBridge.Infrastructure  EF Core (SQL Server), connectors (SMB/SFTP/HTTPS), crypto, the transfer engine.
FileBridge.Worker           Windows Service. Runs Quartz (clustered), the transfer pipeline, retention, SLA,
                            the SMB file watcher, and the tblRunRequest poller (see below).
FileBridge.Admin            ASP.NET Core MVC, IIS-hosted behind a load balancer. Configuration UI, dashboard,
                            history, quarantine review, approvals. Never touches file systems directly.
FileBridge.Tests            xUnit tests for the pure-logic pieces (filters, renaming, schedule windows,
                            retry backoff, file-type sniffing, AES round-trip).
```

**Why the Admin site never touches files directly:** it's load-balanced, so "the node that's handling this
HTTP request" isn't a stable place to run a long file transfer or hold an SFTP session. Every action that
needs file access — Test Connection, Browse, Dry Run, Run Now, Release/Discard Quarantine — is written as a
row in `tblRunRequest`. Any Worker node (any of them; whichever claims the row first) picks it up, executes
it, and writes the result back as JSON. The browser polls `GET /api/v1/requests/{id}` until it's done. This
also means you can add or remove Worker nodes without touching the Admin tier at all.

## Naming convention

- Every enum in `FileBridge.Core/Enums.cs` is the single source of truth for a lookup table. The lookup
  entities (`Lookups.g.cs`), their DDL (`db/01_lookups.sql`), and their seed data (`db/03_seed_lookups.sql`)
  are all **generated from that file** by a small Python script (not checked in, but the pattern is: parse
  the enum, emit `lkp<EnumName>` for each). If you add or rename an enum value, regenerate those three
  things together rather than hand-editing them out of sync.
- Every other table is `tbl<ClassName>` (`tblJob`, `tblTransferHistory`, `tblDataProtectionKey`, ...), set
  via a naming convention in `FileBridgeDbContext.OnModelCreating` rather than per-entity `[Table]`
  attributes, so a new entity gets the right prefix automatically.
- Foreign keys to a lookup are `<EnumName>Id` (e.g. `TransferStatusId` → `lkpTransferStatus.Id`).

## Database setup

Run in order, against a `FileBridge` database:

```
db/01_lookups.sql        -- lkp* tables
db/02_tables.sql          -- tbl* tables (config + operational), FKs to lkp*
db/03_seed_lookups.sql    -- lkp* seed data (from Enums.cs)
db/04_seed_config.sql     -- default tblGlobalSetting rows + a bootstrap admin role mapping
db/05_quartz.sql          -- Quartz.NET schema, table-prefixed to tblQrtz_ (unmodified official script)
db/06_security.sql        -- role + gMSA login placeholders; edit names before running
```

There's no EF Core migrations project — the schema is hand-maintained SQL, and `FileBridgeDbContext` is
mapped to match it. This was a deliberate simplification given the environment; if you'd rather manage
schema via EF migrations, that's a reasonable follow-up (`dotnet ef migrations add Initial` once the code
compiles, reconciling against the SQL above).

## Site configuration

- **Data Protection key ring**: keys live in `tblDataProtectionKey`, encrypted with a certificate whose
  thumbprint is in `DataProtection:CertificateThumbprint` (both Admin and Worker config). The certificate
  must be installed, with its private key, on every node — Admin and Worker alike — or antiforgery tokens
  and stored secrets (SFTP passwords, PGP keys) won't round-trip if a request is served by a different node
  than the one that encrypted it.
- **Auth**: Windows/Negotiate, with both Anonymous and Windows Authentication enabled at the IIS site level
  (`deploy/iis-setup.ps1` sets this up), the app pool running as a gMSA with `useAppPoolCredentials`, and an
  SPN (`HTTP/filebridge.agency.gov`) registered against that gMSA.
- **Health checks**: `/health/live` (always healthy if the process is up) and `/health/ready` (SQL
  connectivity), both anonymous.
- **Antiforgery**: header-based (`X-CSRF-TOKEN`), read by `wwwroot/js/site.js` from a `<meta>` tag, so it
  works the same whichever node serves the page vs. handles the POST.
- **Content-Security-Policy**: `script-src 'self'` — no inline `<script>` or `onclick=...` anywhere in the
  views; every interactive behavior is wired in `site.js` via `data-fb-*` attributes. Bootstrap is vendored
  locally via `libman.json` (`libman restore`), not pulled from a CDN, for the same reason.

## Role mapping

`RoleClaimsTransformation` (Admin/Security) maps the signed-in Windows identity to `tblRoleMapping` rows and
adds `ClaimTypes.Role` claims (`Viewer`, `Operator`, `Admin`, `Approver`). As shipped, it matches on the
**username only** (`DOMAIN\user`), plus a bootstrap list (`Security:BootstrapAdmins` in config) so the first
admin can sign in before any mapping exists. Matching by **AD group** (so `tblRoleMapping` can hold
`AGENCY\FileBridge-Admins` instead of a list of individual users) needs a directory lookup to resolve the
signed-in user's group SIDs to names — that's environment-specific (ADSI, `System.DirectoryServices`, or a
call out to AD) and is left as a `WindowsIdentityLike` stub with a TODO comment; wire up your directory of
choice there.

## Operations runbook

- **Kill switch**: Settings → Global settings → `Engine.KillSwitch`, or the button on the dashboard. When
  on, every Worker node skips every scheduled and manual run — an emergency stop that doesn't require a
  deploy or a service restart.
- **Four-eyes approval**: turn on `Approval.RequireForJobChanges` and job create/edit/delete goes to
  `tblChangeRequest` instead of applying immediately; an Approver (who isn't the requester) applies or
  rejects it from the Approvals page.
- **Quarantine**: a file that fails a checksum, virus scan, or type check is copied to
  `Engine:QuarantineRoot` (a UNC path reachable from every Worker node) and held in `tblQuarantine`. Release
  re-runs it through the pipeline from the held copy; Discard deletes it. Both go through the same
  `tblRunRequest` queue as everything else.
- **Retention**: `RetentionJob` runs nightly (02:00 local to the Worker's OS) and purges old rows per the
  `Retention.*Days` settings, batched at 5,000 rows per delete to avoid long lock waits.
- **Reprocessing a failed file**: History → a failed transfer → "Reprocess" clears its file lease so the
  next scheduled or manual run picks it up again.

## Endpoint types, and how Exchangelka is configured

`Endpoint.EndpointTypeId` picks which connector `EndpointFactory` builds: `Smb` (also used for plain local
disk paths), `Sftp`, or `Https`. `Job.SourceEndpoint` and `Job.DestinationEndpoint` are just two independent
`Endpoint` rows — nothing in the pipeline, the watcher, or job validation assumes one side is a particular
type, or that source and destination differ in type. The only constraint is that they're two different
endpoint rows (`JobService.ValidateAsync` rejects source == destination).

So for this deployment, both the "Exchangelka" network share and the internal SMB file server are `Smb`-type
endpoints, each with its own `BasePath` (a `\\server\share\...` UNC root) and, if needed, its own stored
credential (domain/username/password, protected via Data Protection). `SmbEndpoint` implements the full
`IFileEndpoint` surface — list, download, upload, rename, delete, exists, size, lock-check — directly against
the UNC path with `System.IO`, so both directions (Exchangelka → SMB and SMB → Exchangelka, if you ever need
a job running the other way) work identically. It also gets picked up by `SmbWatcherService`, which sets up
a live `FileSystemWatcher` on `Smb`/`LocalDisk` source endpoints for near-instant triggering instead of
waiting for the next poll.

The `Sftp` and `Https` connectors (`SftpEndpoint`, `HttpsEndpoint`) are still there for any other endpoint
that genuinely isn't reachable as a network share — `HttpsEndpoint` in particular is a generic REST shape
(`GET`/`PUT`/`POST`/`DELETE`) that's a placeholder until you have a specific partner API's real contract;
see the XML doc comment at the top of the file. `IFileEndpoint` is deliberately protocol-neutral, so a new
connector for some other transport doesn't require touching the pipeline, retries, leases, or quarantine.

## Known gaps / next steps

- **Not compiled** (see the warning at the top). Build with `dotnet build FileBridge.sln`, fix compiler
  errors, then `dotnet test` before anything else.
- **AD group → role mapping** resolves by username only until the directory lookup above is wired in.
- **EF Core migrations**: none; schema is the hand-written SQL in `db/`. `FileBridgeDbContext` should match
  it, but a `dotnet ef migrations add Initial --dry-run` comparison once it compiles is worth doing.
- **`HttpsEndpoint`'s REST contract** is a placeholder for whichever partner system ends up using it — see above.
- **`SmbWatcherService` needs the Worker's own identity to already have share access.** It uses a plain
  `FileSystemWatcher`, which runs as the Worker process's identity (the gMSA) — it does not apply an
  endpoint's stored credential the way a transfer does (via `WNetAddConnection2` in `SmbEndpoint.ConnectAsync`).
  If an `Smb`-type source like Exchangelka needs a specific stored credential rather than gMSA access, the
  live watcher won't be able to enumerate it and that job will fall back to the regular scheduled poll
  instead of instant triggering — still correct, just not instant. Grant the gMSA read access to Exchangelka
  directly if you want live triggering there.
- **PGP key formats**: `CryptoService` expects armored ASCII keys passed straight into PgpCore's
  `EncryptionKeys`; binary/keyring formats aren't handled.
- **No integration tests** against real SMB/SFTP/SQL — the test project covers pure logic only, since none
  of those are available in the sandbox this was built in.
