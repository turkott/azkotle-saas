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
