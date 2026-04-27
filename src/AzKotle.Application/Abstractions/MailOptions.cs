namespace AzKotle.Application.Abstractions;

public sealed class MailOptions
{
    public const string SectionName = "Mail";

    /// <summary>
    /// From address (RFC 5322). V produkci doména musí být ověřená u providera.
    /// </summary>
    public string From { get; set; } = string.Empty;
}

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    /// <summary>
    /// API key z Resend dashboardu (re_...). Drží se jen v env varu, nikdy v gitu.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// Veřejná base URL aplikace (frontend). Slouží k sestavení odkazů v emailech
    /// (např. /reset-password?token=...).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>SMTP server hostname (např. smtp.forpsi.com).</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP port. 465 = implicit SSL, 587 = STARTTLS, 25 = plain (nikdy v prod).</summary>
    public int Port { get; set; } = 465;

    /// <summary>SMTP username (typicky plná email adresa, např. noreply@az-kotle.cz).</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>SMTP heslo. Drží se jen v env varu, nikdy v gitu.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// True = implicit SSL/TLS (port 465), false = STARTTLS (port 587). Plain text
    /// SMTP v produkci je out of question — bez TLS se i hashed credentials posílají
    /// vidět.
    /// </summary>
    public bool UseSsl { get; set; } = true;
}
