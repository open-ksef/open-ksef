using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Models;
using NSubstitute;
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
    public void BuildNewInvoicesSummaryMessage_UsesReqTexts()
    {
        var message = CreateService(new EmailOptions()).BuildNewInvoicesSummaryMessage("owner@example.invalid", "NIP 1111111111", 15);

        Assert.Equal("Więcej nowych faktur: NIP 1111111111", message.Subject);
        Assert.Equal("owner@example.invalid", message.To.Mailboxes.Single().Address);
        Assert.Equal(
            "<p>Pobraliśmy z KSeF więcej nowych faktur zakupowych niż wysyłamy osobnych powiadomień. Pozostałe nowe faktury: 15. Zobacz je w aplikacji lub w portalu OpenKSeF.</p>\n" +
            "<p><small>Wysłane przez OpenKSeF</small></p>",
            message.HtmlBody!.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void BuildInviteMessage_UsesReqTexts_HtmlAndPlain_ExpiryInPolishTime()
    {
        var message = CreateService(new EmailOptions()).BuildInviteMessage(
            "zaproszona@example.invalid", "Biuro & <Syn>", "1111111111", TenantRole.Accountant, "Anna Nowak", "https://ksef.example.invalid/zaproszenie#t0k", new DateTime(2026, 11, 30, 23, 30, 0, DateTimeKind.Utc));

        Assert.Equal("Zaproszenie do firmy Biuro & <Syn> w OpenKSeF", message.Subject);
        Assert.Equal(string.Join("\n\n",
            "Dzień dobry,",
            "Anna Nowak zaprasza Cię do firmy Biuro & <Syn> (NIP 1111111111) w OpenKSeF. Twoja rola: Księgowy.",
            "OpenKSeF to aplikacja do przeglądania faktur z Krajowego Systemu e-Faktur (KSeF).",
            "Przyjmij zaproszenie: https://ksef.example.invalid/zaproszenie#t0k",
            "Zaloguj się lub załóż konto na adres zaproszona@example.invalid. Link działa raz i jest ważny do 01.12.2026.",
            "Twój adres e-mail podała osoba zapraszająca. Jeśli nie spodziewasz się tego zaproszenia, zignoruj tę wiadomość. Zaproszenie wygaśnie samo.",
            "Wiadomość wysłana automatycznie przez OpenKSeF."), message.TextBody.ReplaceLineEndings("\n"));
        Assert.Contains("<p>Anna Nowak zaprasza Cię do firmy Biuro &amp; &lt;Syn&gt; (NIP 1111111111) w OpenKSeF. Twoja rola: Księgowy.</p>", message.HtmlBody);
        Assert.Contains("<p><a href=\"https://ksef.example.invalid/zaproszenie#t0k\">Przyjmij zaproszenie</a></p>", message.HtmlBody);
    }

    [Fact]
    public void BuildAccessGrantedMessage_UsesReqTexts_HtmlAndPlain()
    {
        var message = CreateService(new EmailOptions()).BuildAccessGrantedMessage(
            "ksiegowa@example.invalid", "NIP 1111111111", "1111111111", TenantRole.Employee, "Jan Kowalski", "https://ksef.example.invalid");

        Assert.Equal("Masz dostęp do firmy NIP 1111111111", message.Subject);
        Assert.Equal(string.Join("\n\n",
            "Dzień dobry,",
            "masz dostęp do firmy NIP 1111111111 (NIP 1111111111) w OpenKSeF. Twoja rola: Pracownik. Dostęp nadał(a): Jan Kowalski.",
            "Firma jest już na Twojej liście w portalu i w aplikacji mobilnej.",
            "Otwórz portal: https://ksef.example.invalid",
            "Jeśli to pomyłka, możesz opuścić firmę w portalu.",
            "Wiadomość wysłana automatycznie przez OpenKSeF."), message.TextBody.ReplaceLineEndings("\n"));
        Assert.Contains("<a href=\"https://ksef.example.invalid\">", message.HtmlBody);
    }

    [Fact]
    public void BuildIssuedInvoiceStuckMessage_UsesPushTexts_NumberEncoded()
    {
        var message = CreateService(new EmailOptions()).BuildIssuedInvoiceStuckMessage("owner@example.invalid", "FV/<1>/2026");

        Assert.Equal("Faktura czeka na wyjaśnienie w KSeF", message.Subject);
        Assert.Equal(
            "<p>Faktura FV/&lt;1&gt;/2026 wymaga sprawdzenia, szczegóły w portalu. Nie wystawiaj jej ponownie bez sprawdzenia.</p>\n" +
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

    [Theory]
    [InlineData(true, false, "user", 1)]
    [InlineData(true, true, "user", 0)]
    [InlineData(true, false, "", 0)]
    [InlineData(false, false, "user", 0)]
    public void WarnIfPlaintextAuth_WarnsOnlyForEnabledAuthWithoutTls(bool enabled, bool useSsl, string username, int expectedWarnings)
    {
        var logger = Substitute.For<ILogger>();

        EmailService.WarnIfPlaintextAuth(new EmailOptions { Enabled = enabled, UseSsl = useSsl, Username = username }, logger);

        Assert.Equal(expectedWarnings, logger.ReceivedCalls().Count(c => c.GetArguments()[0] is LogLevel.Warning));
    }

    private static int Port(TcpListener listener) => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static EmailService CreateService(EmailOptions options) =>
        new(Options.Create(options), NullLogger<EmailService>.Instance);
}
