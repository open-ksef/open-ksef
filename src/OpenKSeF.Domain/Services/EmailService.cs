using System.Globalization;
using System.Net;
using System.Net.Security;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

public class EmailService : IEmailService
{
    internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl-PL");
    private static readonly TimeZoneInfo PolandTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    private readonly EmailOptions _options;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailOptions> options, ILogger<EmailService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    internal RemoteCertificateValidationCallback? ServerCertificateValidation { get; set; }

    public static void WarnIfPlaintextAuth(EmailOptions options, ILogger logger)
    {
        if (options.Enabled && !options.UseSsl && !string.IsNullOrEmpty(options.Username))
            logger.LogWarning("Email:UseSsl is false and Email:Username is set: SMTP credentials are sent unencrypted");
    }

    public Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt) =>
        SendAsync(() => BuildNewInvoiceMessage(toEmail, evt), evt.TenantId, evt.InvoiceId);

    public Task SendNewInvoicesSummaryEmailAsync(string toEmail, Guid tenantId, string company, int count) =>
        SendAsync(() => BuildNewInvoicesSummaryMessage(toEmail, company, count), tenantId, null);

    public Task SendIssuedInvoiceStuckEmailAsync(string toEmail, Guid tenantId, Guid issuedInvoiceId, string? number) =>
        SendAsync(() => BuildIssuedInvoiceStuckMessage(toEmail, number), tenantId, issuedInvoiceId);

    public Task<bool> SendInviteEmailAsync(string toEmail, Guid tenantId, string company, string nip, TenantRole role, string inviterName, string link, DateTime expiresAtUtc) =>
        SendAsync(() => BuildInviteMessage(toEmail, company, nip, role, inviterName, link, expiresAtUtc), tenantId, null);

    public Task<bool> SendAccessGrantedEmailAsync(string toEmail, Guid tenantId, string company, string nip, TenantRole role, string granterName, string portalUrl) =>
        SendAsync(() => BuildAccessGrantedMessage(toEmail, company, nip, role, granterName, portalUrl), tenantId, null);

    public Task<bool> SendOwnershipClaimedEmailAsync(string toEmail, Guid tenantId, string company, string nip, string newOwnerName) =>
        SendAsync(() => BuildOwnershipClaimedMessage(toEmail, company, nip, newOwnerName), tenantId, null);

    private async Task<bool> SendAsync(Func<MimeMessage> build, Guid tenantId, Guid? invoiceId)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Email notifications disabled, skipping");
            return false;
        }

        var message = build();

        using var timeout = new CancellationTokenSource(SendTimeout);
        using var client = new SmtpClient
        {
            Timeout = (int)SendTimeout.TotalMilliseconds,
            ServerCertificateValidationCallback = ServerCertificateValidation
        };
        try
        {
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, SocketOptions(), timeout.Token);

            if (!string.IsNullOrEmpty(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, timeout.Token);

            await client.SendAsync(message, timeout.Token);
            _logger.LogInformation("Email sent for tenant {TenantId}, invoice {InvoiceId}", tenantId, invoiceId);
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, CancellationToken.None);
        }

        return true;
    }

    internal MimeMessage BuildNewInvoiceMessage(string toEmail, NewInvoiceDetectedEvent evt)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = $"Nowa faktura od {evt.VendorName}";
        message.Body = new TextPart("html")
        {
            Text = $"""
                <p>Otrzymano nową fakturę:</p>
                <ul>
                    <li><strong>Sprzedawca:</strong> {WebUtility.HtmlEncode(evt.VendorName)}</li>
                    <li><strong>Numer faktury:</strong> {WebUtility.HtmlEncode(evt.InvoiceNumber ?? "-")}</li>
                    <li><strong>Kwota:</strong> {evt.Amount.ToString("N2", PolishCulture)} {WebUtility.HtmlEncode(evt.Currency)}</li>
                </ul>
                <p><small>Wysłane przez OpenKSeF</small></p>
                """
        };
        return message;
    }

    internal MimeMessage BuildNewInvoicesSummaryMessage(string toEmail, string company, int count)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = $"Więcej nowych faktur: {company}";
        message.Body = new TextPart("html")
        {
            Text = $"""
                <p>Pobraliśmy z KSeF więcej nowych faktur zakupowych niż wysyłamy osobnych powiadomień. Pozostałe nowe faktury: {count}. Zobacz je w aplikacji lub w portalu OpenKSeF.</p>
                <p><small>Wysłane przez OpenKSeF</small></p>
                """
        };
        return message;
    }

    internal MimeMessage BuildIssuedInvoiceStuckMessage(string toEmail, string? number)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = "Faktura czeka na wyjaśnienie w KSeF";
        message.Body = new TextPart("html")
        {
            Text = $"""
                <p>Faktura {WebUtility.HtmlEncode(number)} wymaga sprawdzenia, szczegóły w portalu. Nie wystawiaj jej ponownie bez sprawdzenia.</p>
                <p><small>Wysłane przez OpenKSeF</small></p>
                """
        };
        return message;
    }

    internal MimeMessage BuildInviteMessage(string toEmail, string company, string nip, TenantRole role, string inviterName, string link, DateTime expiresAtUtc)
    {
        var expires = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc), PolandTimeZone).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return BuildMessage(toEmail, $"Zaproszenie do firmy {company} w OpenKSeF",
            "Dzień dobry,",
            $"{inviterName} zaprasza Cię do firmy {company} (NIP {nip}) w OpenKSeF. Twoja rola: {RoleName(role)}.",
            "OpenKSeF to aplikacja do przeglądania faktur z Krajowego Systemu e-Faktur (KSeF).",
            ("Przyjmij zaproszenie", link),
            $"Zaloguj się lub załóż konto na adres {toEmail}. Link działa raz i jest ważny do {expires}.",
            "Twój adres e-mail podała osoba zapraszająca. Jeśli nie spodziewasz się tego zaproszenia, zignoruj tę wiadomość. Zaproszenie wygaśnie samo.",
            "Wiadomość wysłana automatycznie przez OpenKSeF.");
    }

    internal MimeMessage BuildAccessGrantedMessage(string toEmail, string company, string nip, TenantRole role, string granterName, string portalUrl) =>
        BuildMessage(toEmail, $"Masz dostęp do firmy {company}",
            "Dzień dobry,",
            $"masz dostęp do firmy {company} (NIP {nip}) w OpenKSeF. Twoja rola: {RoleName(role)}. Dostęp nadał(a): {granterName}.",
            "Firma jest już na Twojej liście w portalu i w aplikacji mobilnej.",
            ("Otwórz portal", portalUrl),
            "Jeśli to pomyłka, możesz opuścić firmę w portalu.",
            "Wiadomość wysłana automatycznie przez OpenKSeF.");

    internal MimeMessage BuildOwnershipClaimedMessage(string toEmail, string company, string nip, string newOwnerName) =>
        BuildMessage(toEmail, $"Firma {company} (NIP {nip}) ma nowego właściciela",
            "Dzień dobry,",
            $"Firma {company} (NIP {nip}) ma nowego właściciela: {newOwnerName}. Potwierdził dostęp do KSeF dla tego NIP-u tokenem KSeF. Twój dostęp do firmy w OpenKSeF został usunięty. Jeśli to pomyłka, napisz do administratora serwera.",
            "Wiadomość wysłana automatycznie przez OpenKSeF.");

    private MimeMessage BuildMessage(string toEmail, string subject, params object[] paragraphs)
    {
        var html = paragraphs.Select(p => p is (string text, string url)
            ? $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(text)}</a></p>"
            : $"<p>{WebUtility.HtmlEncode((string)p)}</p>");
        var plain = paragraphs.Select(p => p is (string text, string url) ? $"{text}: {url}" : (string)p);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = string.Join("\n", html), TextBody = string.Join("\n\n", plain) }.ToMessageBody();
        return message;
    }

    private static string RoleName(TenantRole role) => role switch
    {
        TenantRole.Owner => "Właściciel",
        TenantRole.Administrator => "Administrator",
        TenantRole.Accountant => "Księgowy",
        _ => "Pracownik",
    };

    private SecureSocketOptions SocketOptions() =>
        !_options.UseSsl ? SecureSocketOptions.None
        : _options.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTls;
}
