using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;

namespace OpenKSeF.Domain.Tests.Services;

public class EmailServiceTests
{
    private const char Nbsp = (char)160;

    private static readonly NewInvoiceDetectedEvent Event = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Kowalski & <Syn>",
        "FV/1/11/2026",
        1234567.891m,
        "PLN");

    [Fact]
    public void BuildNewInvoiceMessage_UsesPolishTemplate()
    {
        var message = CreateService(new EmailOptions()).BuildNewInvoiceMessage("owner@example.invalid", Event);

        Assert.Equal("Nowa faktura od Kowalski & <Syn>", message.Subject);
        Assert.Equal("owner@example.invalid", message.To.Mailboxes.Single().Address);
        Assert.Equal(
            "<p>Otrzymano nową fakturę:</p>\n" +
            "<ul>\n" +
            "    <li><strong>Sprzedawca:</strong> Kowalski &amp; &lt;Syn&gt;</li>\n" +
            "    <li><strong>Numer faktury:</strong> FV/1/11/2026</li>\n" +
            $"    <li><strong>Kwota:</strong> 1{Nbsp}234{Nbsp}567,89 PLN</li>\n" +
            "</ul>\n" +
            "<p><small>Wysłane przez OpenKSeF</small></p>",
            message.HtmlBody!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task SendNewInvoiceEmailAsync_DoesNotConnect_WhenDisabled()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var options = new EmailOptions { Enabled = false, SmtpHost = "127.0.0.1", SmtpPort = Port(listener) };

        await CreateService(options).SendNewInvoiceEmailAsync("owner@example.invalid", Event);

        Assert.False(listener.Pending());
    }

    [Fact]
    public async Task SendNewInvoiceEmailAsync_GivesUpWithinTenSeconds_WhenServerNeverAnswers()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var options = new EmailOptions { Enabled = true, SmtpHost = "127.0.0.1", SmtpPort = Port(listener), UseSsl = false };
        var stopwatch = Stopwatch.StartNew();

        var exception = await Record.ExceptionAsync(() =>
            CreateService(options).SendNewInvoiceEmailAsync("owner@example.invalid", Event));

        Assert.NotNull(exception);
        Assert.True(listener.Pending());
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(12));
    }

    private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static EmailService CreateService(EmailOptions options) =>
        new(Options.Create(options), NullLogger<EmailService>.Instance);
}
