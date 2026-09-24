using FileBridge.Core;
using Microsoft.EntityFrameworkCore;

namespace FileBridge.Infrastructure.Data;

public class FileBridgeDbContext : DbContext
{
    public FileBridgeDbContext(DbContextOptions<FileBridgeDbContext> options) : base(options)
    {
    }

    public DbSet<Endpoint> Endpoints => Set<Endpoint>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<TransferHistory> TransferHistories => Set<TransferHistory>();
    public DbSet<QuarantineItem> Quarantines => Set<QuarantineItem>();
    public DbSet<RunRequest> RunRequests => Set<RunRequest>();
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();
    public DbSet<RoleMapping> RoleMappings => Set<RoleMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map convention: tbl<ClassName>
        modelBuilder.Entity<Endpoint>().ToTable("tblEndpoint");
        modelBuilder.Entity<Job>().ToTable("tblJob");
        modelBuilder.Entity<TransferHistory>().ToTable("tblTransferHistory");
        modelBuilder.Entity<QuarantineItem>().ToTable("tblQuarantine");
        modelBuilder.Entity<RunRequest>().ToTable("tblRunRequest");
        modelBuilder.Entity<GlobalSetting>().ToTable("tblGlobalSetting").HasKey(s => s.Key);
        modelBuilder.Entity<RoleMapping>().ToTable("tblRoleMapping");

        modelBuilder.Entity<Job>()
            .HasOne(j => j.SourceEndpoint)
            .WithMany()
            .HasForeignKey(j => j.SourceEndpointId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Job>()
            .HasOne(j => j.DestinationEndpoint)
            .WithMany()
            .HasForeignKey(j => j.DestinationEndpointId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
