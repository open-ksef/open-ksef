using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Invoicing;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;
using OpenKSeF.Sync.Issuing;
using Xunit.Abstractions;

namespace OpenKSeF.Domain.Tests.Issuing;

/// <summary>Real send on KSeF TEST (NIP 2 to NIP 1). Opt-in: KSEF_TEST_SEND=1 and open-ksef/.env.test with KSEF_TEST_* values.</summary>
[Trait("Category", "KSeFTest")]
public class KSeFTestSendTests(ITestOutputHelper output)
{
    [KSeFTestFact]
    public async Task SendsOneInvoice_GetsKsefNumberAndUpo()
    {
        var env = File.ReadAllLines(EnvFile())
            .Where(l => l.Contains('=') && !l.TrimStart().StartsWith('#'))
            .Select(l => l.Split('=', 2))
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim().Trim('"', '\''));
        var sellerNip = env["KSEF_TEST_NIP_2"];
        var buyerNip = env["KSEF_TEST_NIP"];

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKSeFClient(o => o.BaseUrl = KsefEnvironmentsUris.TEST);
        services.AddCryptographyClient();
        await using var provider = services.BuildServiceProvider();
        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
        var client = provider.GetRequiredService<IKSeFClient>();
        var crypto = provider.GetRequiredService<ICryptographyService>();
        var gateway = new KSeFGateway(client, provider.GetRequiredService<IAuthCoordinator>(), crypto, NullLogger<KSeFGateway>.Instance);
        var encryption = new AesGcmEncryptionService(RandomNumberGenerator.GetBytes(32));

        var tenantId = Guid.NewGuid();
        var number = $"OKSEF-T199/{DateTime.UtcNow:yyMMddHHmmss}";
        var invoice = Fa3InvoiceWriterTests.Issued(i => i.AddLine("Usługa testowa T-199", null, 1, 100m, VatRate.Rate23),
            sellerNip: sellerNip, buyerNip: buyerNip, number: number, tenantId: tenantId, now: DateTime.UtcNow);
        var written = new Fa3InvoiceWriter().Write(invoice, DateTime.UtcNow);
        Assert.Empty(written.SchemaErrors);
        invoice.AttachSentXml(written.Xml, Convert.ToHexString(SHA256.HashData(written.Xml)), "test", null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var db = new ApplicationDbContext(options))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                UserId = "t199",
                Nip = sellerNip,
                KSeFCredentials = [new KSeFCredential { Id = Guid.NewGuid(), TenantId = tenantId, Type = CredentialType.Token, EncryptedToken = encryption.Encrypt(env["KSEF_TEST_TOKEN_2"]) }]
            });
            db.IssuedInvoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        IssuedInvoice current = invoice;
        var deadline = DateTime.UtcNow.AddMinutes(4);
        while (DateTime.UtcNow < deadline)
        {
            await using var db = new ApplicationDbContext(options);
            await new IssuedInvoiceSendProcessor(db, gateway, new KSeFInvoiceSubmitter(client, crypto), encryption, TimeProvider.System,
                NullLogger<IssuedInvoiceSendProcessor>.Instance).ProcessAsync("test", CancellationToken.None);
            current = await db.IssuedInvoices.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
            if (current.Status == IssuedInvoiceStatus.Rejected || current.UpoXml is not null)
                break;
            await Task.Delay(TimeSpan.FromSeconds(5));
        }

        output.WriteLine($"number={number} status={current.Status} ksef={current.KsefNumber} code={current.KsefStatusCode} " +
                         $"desc={current.KsefStatusDescription} details={current.KsefStatusDetails} lastError={current.LastError} " +
                         $"session={current.KsefSessionRef} element={current.KsefElementRef} upoBytes={current.UpoXml?.Length}");
        Assert.Equal(IssuedInvoiceStatus.Accepted, current.Status);
        Assert.NotNull(current.KsefNumber);
        Assert.NotNull(current.UpoXml);
    }

    private static string EnvFile([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", ".env.test"));

    private sealed class KSeFTestFactAttribute : FactAttribute
    {
        public KSeFTestFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("KSEF_TEST_SEND") != "1")
                Skip = "Real KSeF TEST send; set KSEF_TEST_SEND=1 to run.";
        }
    }
}
