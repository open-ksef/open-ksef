using System.Security.Cryptography;
using System.Xml.Linq;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.DTOs;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using Xunit.Abstractions;

namespace OpenKSeF.Domain.Tests.Services;

/// <summary>Real <see cref="KSeFGateway"/> against KSeF TEST. Opt-in: E2E_TEST_NIP and E2E_TEST_KSEF_TOKEN in the process environment. Output is shapes and counts only: the NIP is real.</summary>
[Trait("Category", "KSeF")]
public class KSeFGatewayContractTests(ITestOutputHelper output)
{
    private const int PageSize = 10;

    [KSeFContractFact]
    public async Task TokenSession_MetadataTwoPages_DownloadAndParseXml()
    {
        var nip = Environment.GetEnvironmentVariable("E2E_TEST_NIP")!;
        var token = Environment.GetEnvironmentVariable("E2E_TEST_KSEF_TOKEN")!;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKSeFClient(o => o.BaseUrl = KsefEnvironmentsUris.TEST);
        services.AddCryptographyClient();
        services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler(() => new KSeFProductionGuard()));
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<KSeFClientOptions>();
        Assert.Equal("api-test.ksef.mf.gov.pl", new Uri(options.BaseUrl).Host);

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        var encryption = new AesGcmEncryptionService(RandomNumberGenerator.GetBytes(32));
        var gateway = new KSeFGateway(
            provider.GetRequiredService<IKSeFClient>(), provider.GetRequiredService<IAuthCoordinator>(),
            provider.GetRequiredService<ICryptographyService>(), encryption,
            provider.GetRequiredService<IHttpClientFactory>(), options, NullLogger<KSeFGateway>.Instance);
        var credential = new KSeFCredential { Id = Guid.NewGuid(), Type = CredentialType.Token, EncryptedToken = encryption.Encrypt(token) };

        var session = await gateway.InitSessionAsync(nip, credential);
        Assert.False(string.IsNullOrEmpty(session.Token));
        Assert.True(session.ExpiresAtUtc > DateTime.UtcNow);

        var to = DateTime.UtcNow;
        var from = to.AddDays(-89);
        var page1 = await gateway.QueryInvoicesAsync(session, new KSeFQueryCriteria(nip, from, to, PageSize, 0));
        var page2 = await gateway.QueryInvoicesAsync(session, new KSeFQueryCriteria(nip, from, to, PageSize, 1));
        output.WriteLine($"page1 rows={page1.Invoices.Count} truncated={page1.IsTruncated}; page2 rows={page2.Invoices.Count} truncated={page2.IsTruncated}");

        Assert.NotEmpty(page1.Invoices);
        Assert.True(page1.Invoices.Count <= PageSize);
        Assert.False(page1.IsTruncated);
        if (page1.Invoices.Count < PageSize)
            Assert.Empty(page2.Invoices);
        Assert.DoesNotContain(page2.Invoices, i => page1.Invoices.Any(p => p.KSeFNumber == i.KSeFNumber));
        Assert.NotNull(page1.LastRowAt);
        Assert.NotNull(page1.ContinuationAt);

        foreach (var header in page1.Invoices.Concat(page2.Invoices))
        {
            Assert.Equal(35, header.KSeFNumber.Length);
            Assert.False(string.IsNullOrEmpty(header.VendorNip));
            Assert.False(string.IsNullOrEmpty(header.InvoiceNumber));
            Assert.Equal(3, header.Currency.Length);
            Assert.Equal(DateTimeKind.Utc, header.IssueDate.Kind);
            Assert.NotEqual(default, header.AcquisitionDate);
        }

        var first = page1.Invoices[0];
        var xml = await gateway.DownloadInvoiceAsync(session, first.KSeFNumber);
        var details = new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance).ExtractInvoiceDetails(xml);
        output.WriteLine($"xml bytes={xml.Length} lines={details.Lines?.Count}");

        Assert.NotSame(InvoiceXmlDetails.Unreadable, details);
        Assert.NotEmpty(details.Lines!);
        Assert.Equal(first.InvoiceNumber, XDocument.Load(new MemoryStream(xml)).Descendants().First(e => e.Name.LocalName == "P_2").Value);
    }

    private sealed class KSeFContractFactAttribute : FactAttribute
    {
        public KSeFContractFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("E2E_TEST_KSEF_TOKEN")) ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("E2E_TEST_NIP")))
                Skip = "KSeF TEST contract test; set E2E_TEST_NIP and E2E_TEST_KSEF_TOKEN to run.";
        }
    }
}
