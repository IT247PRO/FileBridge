using FileBridge.Core.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Infrastructure.Data;

public sealed class FileBridgeDbContext(DbContextOptions<FileBridgeDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<Endpoint> Endpoints => Set<Endpoint>();
    public DbSet<EncryptionProfile> EncryptionProfiles => Set<EncryptionProfile>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobFolderMap> JobFolderMaps => Set<JobFolderMap>();
    public DbSet<FileFilter> FileFilters => Set<FileFilter>();
    public DbSet<SemaphoreRule> SemaphoreRules => Set<SemaphoreRule>();
    public DbSet<PostAction> PostActions => Set<PostAction>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<SlaRule> SlaRules => Set<SlaRule>();
    public DbSet<BlackoutWindow> BlackoutWindows => Set<BlackoutWindow>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<RoleMapping> RoleMappings => Set<RoleMapping>();

    public DbSet<TransferHistory> TransferHistories => Set<TransferHistory>();
    public DbSet<FileLease> FileLeases => Set<FileLease>();
    public DbSet<Quarantine> Quarantines => Set<Quarantine>();
    public DbSet<RunRequest> RunRequests => Set<RunRequest>();
    public DbSet<NodeHeartbeat> NodeHeartbeats => Set<NodeHeartbeat>();
    public DbSet<JobVersion> JobVersions => Set<JobVersion>();
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<ConfigAudit> ConfigAudits => Set<ConfigAudit>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        LookupModel.Register(b);

        b.Entity<Credential>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Endpoint>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.Credential).WithMany().HasForeignKey(x => x.CredentialId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EncryptionProfile>().HasIndex(x => x.Name).IsUnique();

        b.Entity<Job>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.SourceEndpoint).WithMany().HasForeignKey(x => x.SourceEndpointId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DestinationEndpoint).WithMany().HasForeignKey(x => x.DestinationEndpointId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EncryptionProfile).WithMany().HasForeignKey(x => x.EncryptionProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.FolderMaps).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Filters).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SemaphoreRule).WithOne().HasForeignKey<SemaphoreRule>(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PostAction).WithOne().HasForeignKey<PostAction>(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.NotificationRules).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SlaRules).WithOne(x => x.Job).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.BlackoutWindows).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GlobalSetting>().HasKey(x => x.SettingKey);
        b.Entity<RoleMapping>().HasIndex(x => new { x.AdGroup, x.AppRoleId }).IsUnique();

        b.Entity<TransferHistory>(e =>
        {
            e.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.JobId, x.StartedUtc });
        });
        b.Entity<FileLease>(e =>
        {
            e.HasIndex(x => new { x.JobId, x.Fingerprint }).IsUnique();
            e.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Quarantine>(e =>
        {
            e.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<JobFolderMap>().WithMany().HasForeignKey(x => x.FolderMapId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<TransferHistory>().WithMany().HasForeignKey(x => x.TransferHistoryId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<RunRequest>().HasIndex(x => new { x.RequestStatusId, x.Id });
        b.Entity<NodeHeartbeat>().HasKey(x => new { x.NodeName, x.NodeRole });
        b.Entity<JobVersion>(e =>
        {
            e.HasIndex(x => new { x.JobId, x.Version }).IsUnique();
            e.HasOne<Job>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        foreach (var et in b.Model.GetEntityTypes())
        {
            var clr = et.ClrType;

            // Naming convention: lookups -> lkp<Name>, everything else -> tbl<Name>.
            et.SetTableName(typeof(LookupEntity).IsAssignableFrom(clr)
                ? "lkp" + clr.Name[..^"Lookup".Length]
                : "tbl" + clr.Name);

            if (typeof(AuditableEntity).IsAssignableFrom(clr))
                b.Entity(clr).Property(nameof(AuditableEntity.RowVersion)).IsRowVersion();
        }
    }
}
