using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;

namespace OpenKSeF.Domain.Services;

public class EmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailOptions> options, ILogger<EmailService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendNewInvoiceEmailAsync(string toEmail, NewInvoiceDetectedEvent evt)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("Email notifications disabled, skipping");
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = $"New invoice from {evt.VendorName}";
        message.Body = new TextPart("html")
        {
            Text = $"""
                <p>New invoice received:</p>
                <ul>
                    <li><strong>Vendor:</strong> {System.Net.WebUtility.HtmlEncode(evt.VendorName)}</li>
                    <li><strong>Amount:</strong> {evt.Amount:N2} {System.Net.WebUtility.HtmlEncode(evt.Currency)}</li>
                </ul>
                <p><small>Sent by OpenKSeF</small></p>
                """
        };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, _options.UseSsl);

            if (!string.IsNullOrEmpty(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password);

            await client.SendAsync(message);
            _logger.LogInformation("Email sent to {Email} for invoice from {Vendor}", toEmail, evt.VendorName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            throw;
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true);
        }
    }
}
