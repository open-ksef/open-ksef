using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Events;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync;

namespace OpenKSeF.Api.Tests.Controllers;

public class TransferAfterFullResyncTests : IDisposable
{
    private const string UserId = "user-1";
    private const string Account = "PL61 1090 1014 0000 0712 1981 2874";

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private readonly IKSeFGateway _gateway = Substitute.For<IKSeFGateway>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly DateTime _issueDate = DateTime.UtcNow.AddDays(-5);

    [Theory]
    [InlineData("Vat", true, null)]
    [InlineData("Zal", true, "Zaliczka/rozliczenie: wpisz kwotę ręcznie.")]
    [InlineData("Kor", false, "Faktura korygująca: kwota to różnica. Sprawdź, czy jest coś do zapłaty.")]
    public async Task FullResync_FillsStoredType_NoNotification_TransferAppliesTable(string ksefType, bool hasQr, string? message)
    {
        var invoice = await SeedOldRow();
        var before = await GetTransfer(invoice.Id);
        Assert.Null(before.QrCodeBase64);
        Assert.Equal(
            "Nie można ustalić rodzaju faktury. Sprawdź kwotę na fakturze. Aby uzyskać kod QR, uruchom pełną resynchronizację (portal: Firma, zakładka Token KSeF i pobieranie).",
            before.QrMessage);

        _gateway.QueryInvoicesAsync(Arg.Any<KSeFSession>(), Arg.Any<KSeFQueryCriteria>(), Arg.Any<CancellationToken>())
            .Returns(new KSeFInvoiceQueryResult(
                [Header(invoice.KSeFInvoiceNumber, ksefType), Header("KSEF-NOT-STORED-YET", "Vat")], 2, 0, 100));

        var sync = await CreateSyncService().SyncTenantAsync(_tenantId, UserId, forceFullResync: true);

        Assert.Equal(TenantSyncOutcome.Success, sync.Outcome);
        Assert.Equal(1, sync.NewInvoices);
        await _notifications.DidNotReceive().NotifyNewInvoiceAsync(Arg.Any<NewInvoiceDetectedEvent>());

        var stored = await _db.InvoiceHeaders.AsNoTracking().SingleAsync(i => i.Id == invoice.Id);
        Assert.Equal(ksefType, stored.InvoiceType);
        Assert.Equal(Account, stored.VendorBankAccount);

        var after = await GetTransfer(invoice.Id);
        Assert.Equal(hasQr, after.QrCodeBase64 is not null);
        Assert.Equal(message, after.QrMessage);
    }

    private async Task<InvoiceHeader> SeedOldRow()
    {
        _db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            UserId = UserId,
            Nip = "1234567890",
            DisplayName = "Tenant",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            KSeFCredentials =
            [
                new KSeFCredential
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantId,
                    EncryptedToken = "encrypted",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            ]
        });
        var invoice = new InvoiceHeader
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            KSeFInvoiceNumber = "KSEF-OLD-1",
            KSeFReferenceNumber = "KSEF-OLD-1",
            InvoiceNumber = "FV 1/2026",
            VendorName = "Odbiorca 1",
            VendorNip = "1234567890",
            AmountGross = 100m,
            Currency = "PLN",
            IssueDate = _issueDate,
            VendorBankAccount = Account,
            InvoiceType = null,
            FirstSeenAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow
        };
        _db.InvoiceHeaders.Add(invoice);
        await _db.SaveChangesAsync();
        return invoice;
    }

    private KSeFInvoiceHeader Header(string number, string invoiceType) => new(
        number, number, "FV 1/2026", "1234567890", "Odbiorca 1", null, null,
        81.30m, 18.70m, 100m, "PLN", _issueDate, _issueDate, invoiceType);

    private async Task<TransferDetailsResponse> GetTransfer(Guid id)
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(UserId);
        var controller = new InvoicesController(_db, currentUser, new TransferDetailsService(), new QrCodeService());
        var result = await controller.GetTransferDetails(_tenantId, id) as OkObjectResult;
        return Assert.IsType<TransferDetailsResponse>(result!.Value);
    }

    private TenantSyncService CreateSyncService() => new(
        _db,
        _gateway,
        Substitute.For<IEncryptionService>(),
        new InvoiceService(_db, _notifications, NullLogger<InvoiceService>.Instance),
        new KSeFInvoiceXmlParser(NullLogger<KSeFInvoiceXmlParser>.Instance),
        Options.Create(new TenantSyncOptions()),
        NullLogger<TenantSyncService>.Instance);

    public void Dispose() => _db.Dispose();
}
