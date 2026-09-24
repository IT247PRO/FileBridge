using System.Text;

namespace FileBridge.Core.Rules;

/// <summary>Identifies content by magic bytes so a renamed .exe cannot pass as .csv.</summary>
public static class FileTypeSniffer
{
    private static readonly byte[] PgpPacketTags = [0x84, 0x85, 0x8C, 0x99, 0x95, 0xA3, 0xA8, 0xC1, 0xC3, 0xC6];

    public static async Task<string> DetectAsync(string path, CancellationToken ct)
    {
        var buffer = new byte[4096];
        await using var fs = File.OpenRead(path);
        var read = await fs.ReadAsync(buffer, ct);
        return Detect(buffer.AsSpan(0, read));
    }

    public static string Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length == 0) return "empty";
        if (h.StartsWith("%PDF"u8)) return "pdf";
        if (h.StartsWith("PK\u0003\u0004"u8) || h.StartsWith("PK\u0005\u0006"u8)) return "zip";
        if (h.Length >= 2 && h[0] == 0x1F && h[1] == 0x8B) return "gzip";
        if (h.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 })) return "png";
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpeg";
        if (h.StartsWith("GIF8"u8)) return "gif";
        if (h.StartsWith("II*\0"u8) || h.StartsWith("MM\0*"u8)) return "tiff";
        if (h.StartsWith("MZ"u8)) return "executable";
        if (h.StartsWith("-----BEGIN PGP"u8)) return "pgp";
        if (Array.IndexOf(PgpPacketTags, h[0]) >= 0 && h.IndexOf((byte)0) >= 0) return "pgp";
        if (h.IndexOf((byte)0) >= 0) return "binary";

        var text = Encoding.UTF8.GetString(h).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || text.StartsWith('<')) return "xml";
        if (text.StartsWith('{') || text.StartsWith('[')) return "json";
        var firstLine = text.Split('\n')[0];
        if (firstLine.Contains(',') || firstLine.Contains('|') || firstLine.Contains('\t')) return "csv";
        return "text";
    }

    /// <summary>"text" in the allow list also admits csv, xml and json.</summary>
    public static bool IsAllowed(string detected, string allowedCsv)
    {
        var allowed = allowedCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToLowerInvariant()).ToHashSet();
        if (allowed.Contains(detected)) return true;
        return allowed.Contains("text") && detected is "csv" or "xml" or "json" or "text";
    }
}
