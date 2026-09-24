using FileBridge.Core;
using FileBridge.Infrastructure.Connectors;
using FileBridge.Infrastructure.Crypto;
using FileBridge.Infrastructure.Data;
using FileBridge.Infrastructure.Engine;
using FileBridge.Infrastructure.Notifications;
using FileBridge.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FileBridge.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Shared by Admin and Worker: SQL, shared Data Protection key ring, secrets, notifications, heartbeat.</summary>
    public static IServiceCollection AddFileBridgeInfrastructure(this IServiceCollection services, IConfiguration config, string nodeRole)
    {
        var cs = config.GetConnectionString("FileBridge")
                 ?? throw new InvalidOperationException("ConnectionStrings:FileBridge is required.");

        services.AddScoped<ConfigAuditInterceptor>();
        services.AddDbContextFactory<FileBridgeDbContext>((sp, o) => o
            .UseSqlServer(cs, sql => { sql.EnableRetryOnFailure(5); sql.CommandTimeout(120); })
            .AddInterceptors(sp.GetRequiredService<ConfigAuditInterceptor>()), ServiceLifetime.Scoped);
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<FileBridgeDbContext>>().CreateDbContext());

        // One key ring for the whole cluster: every Admin node behind the load balancer and every Worker
        // must read the same keys, otherwise antiforgery tokens and stored secrets break across nodes.
        var dp = services.AddDataProtection()
            .SetApplicationName("FileBridge")
            .PersistKeysToDbContext<FileBridgeDbContext>();
        var thumbprint = config["DataProtection:CertificateThumbprint"];
        if (!string.IsNullOrWhiteSpace(thumbprint)) dp.ProtectKeysWithCertificate(thumbprint);

        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddMemoryCache();
        services.Configure<NotificationOptions>(config.GetSection("Notifications"));
        services.AddHttpClient("teams", c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<INotifier, Notifier>();

        services.AddHostedService(sp => new NodeHeartbeatService(
            sp.GetRequiredService<IServiceScopeFactory>(), nodeRole,
            sp.GetRequiredService<ILogger<NodeHeartbeatService>>()));
        return services;
    }

    /// <summary>Worker only: connectors and the transfer engine.</summary>
    public static IServiceCollection AddFileBridgeEngine(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<EngineOptions>(config.GetSection("Engine"));
        // Transfers can run for a long time; the pipeline owns retries, so no client-level timeout or retry here.
        services.AddHttpClient("https-endpoint", c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IEndpointFactory, EndpointFactory>();
        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IAntivirusScanner, DefenderCliScanner>();
        services.AddScoped<TransferPipeline>();
        services.AddScoped<RetentionService>();
        services.AddScoped<SlaEvaluator>();
        return services;
    }
}
