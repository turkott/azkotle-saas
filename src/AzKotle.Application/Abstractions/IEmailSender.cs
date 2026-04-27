namespace AzKotle.Application.Abstractions;

/// <summary>
/// Provider-neutrální abstrakce pro odesílání transactional emailů. Záměrně
/// úzké API — žádné attachments ani template bindings v MVP. Konkrétní
/// implementace (Resend, SMTP) jsou v Infrastructure.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string TextBody);
