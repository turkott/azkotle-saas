using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AzKotle.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzKotle.Infrastructure.External;

/// <summary>
/// Resend (https://resend.com) implementace <see cref="IEmailSender"/>.
/// Volá REST endpoint POST https://api.resend.com/emails s Bearer auth.
/// API key (re_...) drží <see cref="ResendOptions"/> z konfigurace; nikdy
/// se neloguje.
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    public const string HttpClientName = "resend";

    private readonly HttpClient _http;
    private readonly MailOptions _mail;
    private readonly ResendOptions _resend;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        HttpClient http,
        IOptions<MailOptions> mail,
        IOptions<ResendOptions> resend,
        ILogger<ResendEmailSender> logger)
    {
        _http = http;
        _mail = mail.Value;
        _resend = resend.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_resend.ApiKey))
        {
            throw new InvalidOperationException(
                "Resend API key není nakonfigurovaný (Resend:ApiKey).");
        }

        if (string.IsNullOrWhiteSpace(_mail.From))
        {
            throw new InvalidOperationException(
                "Mail From adresa není nakonfigurovaná (Mail:From).");
        }

        var payload = new ResendPayload(
            From: _mail.From,
            To: new[] { message.To },
            Subject: message.Subject,
            Html: message.HtmlBody,
            Text: message.TextBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _resend.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            // Záměrně logujeme jen redacted info — body může obsahovat email adresy.
            _logger.LogError("Resend send failed: {Status}. Body length={Length}",
                (int)response.StatusCode, body.Length);
            response.EnsureSuccessStatusCode();
        }
    }

    private sealed record ResendPayload(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] IReadOnlyCollection<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);
}
