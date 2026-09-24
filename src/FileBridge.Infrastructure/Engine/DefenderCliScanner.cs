using System.Diagnostics;
using FileBridge.Core;
using Microsoft.Extensions.Options;

namespace FileBridge.Infrastructure.Engine;

/// <summary>On-demand scan with Microsoft Defender (MpCmdRun). Exit 0 = clean, 2 = threat; anything else is retried.</summary>
public sealed class DefenderCliScanner(IOptions<EngineOptions> options) : IAntivirusScanner
{
    public async Task<ScanResult> ScanAsync(string path, CancellationToken ct)
    {
        var exe = options.Value.DefenderPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            throw new NonRetryableException($"Virus scanning is on for this job, but the scanner was not found at '{exe}'.");

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[] { "-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation" }) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the virus scanner.");
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return proc.ExitCode switch
        {
            0 => new ScanResult(true, "Clean"),
            2 => new ScanResult(false, "Virus scan found a threat: " + stdout.Trim().Split('\n').LastOrDefault()?.Trim()),
            _ => throw new IOException($"Virus scanner exited with code {proc.ExitCode}.")
        };
    }
}
