using System.IO.Compression;
using FileBridge.Core;
using FileBridge.Core.Rules;

namespace FileBridge.Infrastructure.Engine;

internal static class Compression
{
    public static string Zip(string input, string outDir)
    {
        var output = Path.Combine(outDir, Path.GetFileName(input) + ".zip");
        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(input, Path.GetFileName(input), CompressionLevel.Optimal);
        return output;
    }

    /// <summary>Flattens entries, blocks path traversal, and caps total size and entry count (zip-bomb guard).</summary>
    public static IReadOnlyList<string> Unzip(string input, string outDir, long maxBytes, int maxEntries)
    {
        using var zip = ZipFile.OpenRead(input);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (entries.Count > maxEntries) throw new QuarantineException($"Archive has {entries.Count} entries (limit {maxEntries}).");
        if (entries.Sum(e => e.Length) > maxBytes) throw new QuarantineException("Archive expands beyond the configured size limit.");

        var outputs = new List<string>();
        foreach (var e in entries)
        {
            var name = RemotePath.Sanitize(Path.GetFileName(e.FullName));
            var target = Path.Combine(outDir, name);
            if (File.Exists(target)) target = Path.Combine(outDir, $"{Path.GetFileNameWithoutExtension(name)}_{outputs.Count}{Path.GetExtension(name)}");
            e.ExtractToFile(target);
            outputs.Add(target);
        }
        return outputs;
    }
}
