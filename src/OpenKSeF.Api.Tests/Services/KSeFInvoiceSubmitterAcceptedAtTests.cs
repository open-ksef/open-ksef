using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Invoices;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Services;
using OpenKSeF.Sync.Issuing;

using CirfmfKSeFClient = KSeF.Client.Core.Interfaces.Clients.IKSeFClient;

namespace OpenKSeF.Api.Tests.Services;

public class KSeFInvoiceSubmitterAcceptedAtTests
{
    private const string KsefNumber = "1111111111-20261102-0100A0B0C0D0-E1";
    private static readonly DateTimeOffset Acquired = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Stored = new(2026, 11, 2, 8, 0, 5, TimeSpan.Zero);

    private readonly CirfmfKSeFClient _client = Substitute.For<CirfmfKSeFClient>();
    private InvoiceQueryFilters? _sent;

    private Task<DateTimeOffset?> AcceptedAt(string ksefNumber, params InvoiceSummary[] rows)
    {
        _client.QueryInvoiceMetadataAsync(
                Arg.Do<InvoiceQueryFilters>(f => _sent = f),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<SortOrder>(),
                Arg.Any<CancellationToken>())
            .Returns(new PagedInvoiceResponse { Invoices = rows.ToList() });

        var submitter = new KSeFInvoiceSubmitter(_client, Substitute.For<ICryptographyService>(), Substitute.For<IEncryptionService>(), Substitute.For<IKSeFGateway>());
        return submitter.GetAcceptedAtAsync("token", ksefNumber);
    }

    [Fact]
    public async Task QueriesByKsefNumber_AroundNumberDate_OnPermanentStorage_WithoutHwm()
    {
        await AcceptedAt(KsefNumber);

        Assert.Equal(KsefNumber, _sent!.KsefNumber);
        Assert.Equal(InvoiceSubjectType.Subject1, _sent.SubjectType);
        Assert.Equal(DateType.PermanentStorage, _sent.DateRange.DateType);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), _sent.DateRange.From);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), _sent.DateRange.To);
        Assert.Null(_sent.DateRange.RestrictToPermanentStorageHwmDate);
    }

    [Fact]
    public async Task PrefersPermanentStorageDate()
    {
        Assert.Equal(Stored, await AcceptedAt(KsefNumber, new InvoiceSummary { KsefNumber = KsefNumber, AcquisitionDate = Acquired, PermanentStorageDate = Stored }));
    }

    [Fact]
    public async Task FallsBackToAcquisitionDate_WhenPermanentStorageDateMissing()
    {
        Assert.Equal(Acquired, await AcceptedAt(KsefNumber, new InvoiceSummary { KsefNumber = KsefNumber, AcquisitionDate = Acquired }));
    }

    [Fact]
    public async Task NoMatchingRow_ReturnsNull()
    {
        Assert.Null(await AcceptedAt(KsefNumber, new InvoiceSummary { KsefNumber = "1111111111-20261102-0100A0B0C0D0-FF", PermanentStorageDate = Stored }));
    }

    [Fact]
    public async Task UnparsableKsefNumber_ReturnsNull_WithoutQuery()
    {
        Assert.Null(await AcceptedAt("KSEF-1"));
        Assert.Null(_sent);
    }
}
