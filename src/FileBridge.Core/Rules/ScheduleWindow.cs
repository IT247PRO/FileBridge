using FileBridge.Core.Entities;

namespace FileBridge.Core.Rules;

public static class ScheduleWindow
{
    public static int ToMask(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (m, d) => m | (1 << (int)d));
    public static IEnumerable<DayOfWeek> FromMask(int mask) => Enum.GetValues<DayOfWeek>().Where(d => (mask & (1 << (int)d)) != 0);

    /// <summary>True when local time is inside the day mask and time window. Supports overnight windows (22:00-04:00).
    /// Takes primitive fields (not a Job) so ProcessJob can share this without a Job-specific dependency.</summary>
    public static bool IsWithinActiveWindow(int activeDaysMask, TimeOnly? activeFromTime, TimeOnly? activeToTime, DateTime local)
    {
        if ((activeDaysMask & (1 << (int)local.DayOfWeek)) == 0) return false;
        if (activeFromTime is not { } from || activeToTime is not { } to) return true;
        var t = TimeOnly.FromDateTime(local);
        return from <= to ? t >= from && t <= to : t >= from || t <= to;
    }

    public static bool IsInBlackout(IEnumerable<BlackoutWindow> windows, DateTime utc) =>
        windows.Any(w => utc >= w.StartUtc && utc < w.EndUtc);

    public static TimeZoneInfo FindZone(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
        return TimeZoneInfo.Local;
    }

    public static DateTimeOffset LocalNow(string? timeZoneId) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, FindZone(timeZoneId));
}
