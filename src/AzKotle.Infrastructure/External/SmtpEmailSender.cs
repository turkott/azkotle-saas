using AzKotle.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AzKotle.Infrastructure.External;

/// <summary>
/// SMTP implementace <see cref="IEmailSender"/> přes MailKit. Alternativa k
/// <see cref="ResendEmailSender"/> — Forpsi mailbox `noreply@az-kotle.cz` posílá
/// přes `smtp.forpsi.com:465` (implicit SSL). Bez ověření domény u třetí strany,
/// využívá existující Forpsi mail účet.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly MailOptions _mail;
    private readonly SmtpOptions _smtp;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IOptions<MailOptions> mail,
        IOptions<SmtpOptions> smtp,
        ILogger<SmtpEmailSender> logger)
    {
        _mail = mail.Value;
        _smtp = smtp.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_smtp.Host))
        {
            throw new InvalidOperationException("SMTP host není nakonfigurovaný (Smtp:Host).");
        }

        if (string.IsNullOrWhiteSpace(_mail.From))
        {
            throw new InvalidOperationException("Mail From adresa není nakonfigurovaná (Mail:From).");
        }

        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(_mail.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        var builder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
        };
        mime.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            // SecureSocketOptions.SslOnConnect = port 465 implicit TLS.
            // StartTls = port 587. Auto = MailKit zvolí podle portu (465 = SSL).
            var secure = _smtp.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(_smtp.Host, _smtp.Port, secure, ct);

            if (!string.IsNullOrWhiteSpace(_smtp.User))
            {
                await client.AuthenticateAsync(_smtp.User, _smtp.Password, ct);
            }

            await client.SendAsync(mime, ct);
        }
        finally
        {
            // DisconnectAsync čeká na QUIT odpověď serveru — bez try/finally
            // se zaseknutý DisconnectAsync zablokuje další pokus o send. Použijeme
            // tight timeout, ale i kdyby selhal, klientský object disposeujeme.
            try
            {
                using var disconnectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.DisconnectAsync(quit: true, disconnectTimeout.Token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SMTP disconnect failed (non-fatal)");
            }
        }
    }
}
