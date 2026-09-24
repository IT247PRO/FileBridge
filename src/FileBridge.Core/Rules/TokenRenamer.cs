using System.Globalization;
using System.Text.RegularExpressions;

namespace FileBridge.Core.Rules;

/// <summary>
/// Tokens: {filename} {name} {ext} {job} {node} {guid} {date:yyyyMMdd} (job-local time) {utc:HHmmss}.
/// </summary>
public static partial class TokenRenamer
{
    [GeneratedRegex(@"\{(date|utc):([^}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex DateToken();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Apply(string? pattern, string fileName, string jobName, DateTimeOffset nowLocal)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return fileName;
        var result = DateToken().Replace(pattern, m =>
        {
            var when = m.Groups[1].Value.Equals("utc", StringComparison.OrdinalIgnoreCase) ? nowLocal.UtcDateTime : nowLocal.DateTime;
            return when.ToString(m.Groups[2].Value, CultureInfo.InvariantCulture);
        });
        result = ApplyNameTokens(result, fileName)
            .Replace("{job}", RemotePath.Sanitize(Whitespace().Replace(jobName.Trim(), "_")), StringComparison.OrdinalIgnoreCase)
            .Replace("{node}", Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            .Replace("{guid}", Guid.NewGuid().ToString("N"), StringComparison.OrdinalIgnoreCase);
        return RemotePath.Sanitize(result);
    }

    public static string ApplyNameTokens(string pattern, string fileName) => pattern
        .Replace("{filename}", fileName, StringComparison.OrdinalIgnoreCase)
        .Replace("{name}", Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase)
        .Replace("{ext}", Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase);
}
