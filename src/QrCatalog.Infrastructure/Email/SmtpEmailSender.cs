using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using QrCatalog.Application.Abstractions;

namespace QrCatalog.Infrastructure.Email;

/// <summary>
/// MailKit üzərindən SMTP. Konfiqurasiya: Email:Smtp:{Host,Port,User,Password,From}.
/// Host verilməyibsə noop — dev-də SMTP olmadan hər şey işləyir, sadəcə loglanır.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(
        IReadOnlyCollection<string> to, string subject, string textBody,
        CancellationToken ct = default)
    {
        var host = _config["Email:Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation(
                "SMTP konfiqurasiya olunmayıb — e-poçt göndərilmədi: {Subject} → {To}",
                subject, string.Join(", ", to));
            return;
        }

        if (to.Count == 0)
            return;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_config["Email:Smtp:From"] ?? "no-reply@localhost"));
        foreach (var address in to)
            message.To.Add(MailboxAddress.Parse(address));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = textBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(host,
            int.TryParse(_config["Email:Smtp:Port"], out var port) ? port : 587,
            SecureSocketOptions.StartTlsWhenAvailable, ct);

        var user = _config["Email:Smtp:User"];
        if (!string.IsNullOrWhiteSpace(user))
            await client.AuthenticateAsync(user, _config["Email:Smtp:Password"] ?? "", ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
