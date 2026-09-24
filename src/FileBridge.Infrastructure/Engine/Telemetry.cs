using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FileBridge.Infrastructure.Engine;

public static class Telemetry
{
    public const string Name = "FileBridge.Engine";
    public static readonly ActivitySource Source = new(Name);
    private static readonly Meter Meter = new(Name);
    public static readonly Counter<long> Files = Meter.CreateCounter<long>("filebridge.files", description: "Files processed, tagged by job and status");
    public static readonly Counter<long> Bytes = Meter.CreateCounter<long>("filebridge.bytes", "By", "Bytes delivered");
}
