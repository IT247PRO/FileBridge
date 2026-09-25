using System.Net.Http.Json;
using FileBridge.Core;
using FileBridge.Infrastructure.Data;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace FileBridge.Infrastructure.Notifications;

/// <summary>Sends to every enabled rule for the event (job-specific and global). Never throws: alerts must not fail transfers.</summary>
public sealed class Notifier(FileBridgeDbContext db, ISecretProtector secrets, IHttpClientFactory http,
    IOptions<NotificationOptions> options, ILogger<Notifier> log) : INotifier
{
    public async Task NotifyAsync(NotificationEvent evt, int? jobId, string subject, string body, CancellationToken ct, int? processJobId = null)
    {
        // Process-job rules are matched exactly (no "global" row for them, unlike transfer jobs' JobId == null),
        // since a NotificationRule with both owner columns null already means "every transfer job" and reusing
        // that for process jobs too would silently change what existing global transfer-job rules fire for.
        var rules = await db.NotificationRules.AsNoTracking()
            .Where(r => r.IsEnabled && r.NotificationEventId == evt &&
                (processJobId != null ? r.ProcessJobId == processJobId : (r.JobId == null || r.JobId == jobId)))
            .ToListAsync(ct);
        var link = processJobId is not null
            ? $"{options.Value.AdminUrl.TrimEnd('/')}/ProcessJobs/History?id={processJobId}"
            : $"{options.Value.AdminUrl.TrimEnd('/')}/{(jobId is null ? "" : $"History?jobId={jobId}")}";

        foreach (var rule in rules)
        {
            try
            {
                if (rule.NotificationChannelId == NotificationChannel.Email) await EmailAsync(rule.Target, subject, $"{body}\n\n{link}", ct);
                else await TeamsAsync(secrets.Unprotect(rule.Target) ?? "", subject, $"{body}\n\n{link}", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Notification rule {RuleId} ({Channel}) failed for {Event}", rule.Id, rule.NotificationChannelId, evt);
            }
        }
    }

    private async Task EmailAsync(string recipients, string subject, string body, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SmtpHost)) { log.LogWarning("SMTP host not configured; email skipped"); return; }
        var msg = new MimeMessage { Subject = subject, Body = new TextPart("plain") { Text = body } };
        msg.From.Add(MailboxAddress.Parse(o.From));
        foreach (var addr in recipients.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            msg.To.Add(MailboxAddress.Parse(addr));

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(o.SmtpHost, o.SmtpPort, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(o.Username)) await smtp.AuthenticateAsync(o.Username, o.Password ?? "", ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(true, ct);
    }

    private async Task TeamsAsync(string webhook, string subject, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(webhook)) return;
        using var resp = await http.CreateClient("teams").PostAsJsonAsync(webhook, new { text = $"**{subject}**\n\n{body}" }, ct);
        resp.EnsureSuccessStatusCode();
    }
}
