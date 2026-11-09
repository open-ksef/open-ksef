using System.Globalization;
using System.Net;
using System.Net.Security;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

public class EmailService : IEmailService
{
    internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl-PL");

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

    private async Task SendAsync(Func<MimeMessage> build, Guid tenantId, Guid? invoiceId)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Email notifications disabled, skipping");
            return;
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

    private SecureSocketOptions SocketOptions() =>
        !_options.UseSsl ? SecureSocketOptions.None
        : _options.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect
        : SecureSocketOptions.StartTls;
}
