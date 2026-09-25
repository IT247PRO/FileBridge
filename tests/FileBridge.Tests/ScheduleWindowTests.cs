using FileBridge.Core.Entities;
using FileBridge.Core.Rules;
using Xunit;

namespace FileBridge.Tests;

public class ScheduleWindowTests
{
    [Fact]
    public void MaskRoundTrips()
    {
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday };
        var mask = ScheduleWindow.ToMask(days);
        Assert.Equal(days.OrderBy(d => d), ScheduleWindow.FromMask(mask).OrderBy(d => d));
    }

    [Fact]
    public void NormalWindowWithinRange()
    {
        Assert.True(ScheduleWindow.IsWithinActiveWindow(127, new TimeOnly(8, 0), new TimeOnly(17, 0), new DateTime(2026, 1, 5, 12, 0, 0))); // Monday noon
        Assert.False(ScheduleWindow.IsWithinActiveWindow(127, new TimeOnly(8, 0), new TimeOnly(17, 0), new DateTime(2026, 1, 5, 19, 0, 0)));
    }

    [Fact]
    public void OvernightWindowWraps()
    {
        Assert.True(ScheduleWindow.IsWithinActiveWindow(127, new TimeOnly(22, 0), new TimeOnly(4, 0), new DateTime(2026, 1, 5, 23, 0, 0)));
        Assert.True(ScheduleWindow.IsWithinActiveWindow(127, new TimeOnly(22, 0), new TimeOnly(4, 0), new DateTime(2026, 1, 5, 2, 0, 0)));
        Assert.False(ScheduleWindow.IsWithinActiveWindow(127, new TimeOnly(22, 0), new TimeOnly(4, 0), new DateTime(2026, 1, 5, 12, 0, 0)));
    }

    [Fact]
    public void DayMaskExcludesDay()
    {
        var mask = ScheduleWindow.ToMask(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday });
        Assert.False(ScheduleWindow.IsWithinActiveWindow(mask, null, null, new DateTime(2026, 1, 5, 12, 0, 0))); // Monday
        Assert.True(ScheduleWindow.IsWithinActiveWindow(mask, null, null, new DateTime(2026, 1, 3, 12, 0, 0)));  // Saturday
    }

    [Fact]
    public void BlackoutDetected()
    {
        var windows = new[] { new BlackoutWindow { StartUtc = new DateTime(2026, 1, 1), EndUtc = new DateTime(2026, 1, 2) } };
        Assert.True(ScheduleWindow.IsInBlackout(windows, new DateTime(2026, 1, 1, 12, 0, 0)));
        Assert.False(ScheduleWindow.IsInBlackout(windows, new DateTime(2026, 1, 3)));
    }
}
