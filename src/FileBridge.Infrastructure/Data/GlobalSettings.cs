using Microsoft.EntityFrameworkCore;

namespace FileBridge.Infrastructure.Data;

public static class SettingKeys
{
    public const string KillSwitch = "Engine.KillSwitch";
    public const string RequireApproval = "Approval.RequireForJobChanges";
    public const string HistoryDays = "Retention.HistoryDays";
    public const string LeaseDays = "Retention.LeaseDays";
    public const string RequestDays = "Retention.RequestDays";
    public const string AuditDays = "Retention.AuditDays";
    public const string LogDays = "Retention.LogDays";
    public const string QuarantineDays = "Retention.QuarantineDays";
}

public static class GlobalSettings
{
    public static Task<string?> GetAsync(FileBridgeDbContext db, string key, CancellationToken ct) =>
        db.GlobalSettings.AsNoTracking().Where(s => s.SettingKey == key).Select(s => s.SettingValue).FirstOrDefaultAsync(ct);

    public static async Task<bool> GetBoolAsync(FileBridgeDbContext db, string key, CancellationToken ct) =>
        bool.TryParse(await GetAsync(db, key, ct), out var b) && b;

    public static async Task<int> GetIntAsync(FileBridgeDbContext db, string key, int fallback, CancellationToken ct) =>
        int.TryParse(await GetAsync(db, key, ct), out var i) && i > 0 ? i : fallback;
}
