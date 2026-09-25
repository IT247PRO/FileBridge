using System.Globalization;
using System.Text.RegularExpressions;

namespace FileBridge.Core.Rules;

/// <summary>
/// Tokens for a ProcessJob's Arguments: {job} {node} {guid} {date:yyyyMMdd} (job-local time) {utc:HHmmss}.
/// Unlike TokenRenamer, output is not path-sanitized: arguments are free-form command-line text, not file names.
/// </summary>
public static partial class ProcessArgTemplater
{
    [GeneratedRegex(@"\{(date|utc):([^}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex DateToken();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Apply(string? pattern, string jobName, DateTimeOffset nowLocal)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return "";
        var result = DateToken().Replace(pattern, m =>
        {
            var when = m.Groups[1].Value.Equals("utc", StringComparison.OrdinalIgnoreCase) ? nowLocal.UtcDateTime : nowLocal.DateTime;
            return when.ToString(m.Groups[2].Value, CultureInfo.InvariantCulture);
        });
        return result
            .Replace("{job}", Whitespace().Replace(jobName.Trim(), "_"), StringComparison.OrdinalIgnoreCase)
            .Replace("{node}", Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            .Replace("{guid}", Guid.NewGuid().ToString("N"), StringComparison.OrdinalIgnoreCase);
    }
}
