using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using FileBridge.Core.Entities;

namespace FileBridge.Core.Rules;

public static class FileFilterEvaluator
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new();
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>* = any chars except '/', ** = any depth, ? = one char.</summary>
    public static Regex GlobToRegex(string glob) => Cache.GetOrAdd("g:" + glob, g =>
    {
        var sb = new StringBuilder("^");
        var p = g[2..];
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            if (c == '*')
            {
                if (i + 1 < p.Length && p[i + 1] == '*')
                {
                    sb.Append(".*");
                    i++;
                    if (i + 1 < p.Length && p[i + 1] == '/') i++;
                }
                else sb.Append("[^/]*");
            }
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        sb.Append('$');
        return new Regex(sb.ToString(), Opts, TimeSpan.FromSeconds(1));
    });

    public static bool PatternMatches(FileFilter filter, RemoteFile file)
    {
        var pattern = string.IsNullOrWhiteSpace(filter.Pattern) ? "*" : filter.Pattern.Trim();
        var target = filter.IsRegex || !pattern.Contains('/') ? file.Name : file.Path;
        var rx = filter.IsRegex
            ? Cache.GetOrAdd("r:" + pattern, k => new Regex(k[2..], Opts, TimeSpan.FromSeconds(1)))
            : GlobToRegex(pattern);
        return rx.IsMatch(target);
    }

    /// <summary>Any exclude match rejects. If includes exist, at least one must match and pass its limits.</summary>
    public static bool IsMatch(RemoteFile file, IReadOnlyCollection<FileFilter> filters, DateTimeOffset nowUtc)
    {
        if (filters.Where(f => f.IsExclude).Any(f => PatternMatches(f, file))) return false;
        var includes = filters.Where(f => !f.IsExclude).ToList();
        if (includes.Count == 0) return true;
        return includes.Any(f => PatternMatches(f, file) && WithinLimits(f, file, nowUtc));
    }

    private static bool WithinLimits(FileFilter f, RemoteFile file, DateTimeOffset nowUtc)
    {
        if (f.MinSizeBytes is { } min && file.Size < min) return false;
        if (f.MaxSizeBytes is { } max && file.Size > max) return false;
        if (f.MinAgeSeconds is { } age && file.LastModifiedUtc > nowUtc.AddSeconds(-age)) return false;
        return true;
    }

    public static bool TryValidate(FileFilter f, out string? error)
    {
        error = null;
        try
        {
            if (f.IsRegex) _ = new Regex(f.Pattern, Opts, TimeSpan.FromSeconds(1));
            else _ = GlobToRegex(f.Pattern);
            return true;
        }
        catch (ArgumentException ex) { error = $"Pattern '{f.Pattern}' is invalid: {ex.Message}"; return false; }
    }
}
