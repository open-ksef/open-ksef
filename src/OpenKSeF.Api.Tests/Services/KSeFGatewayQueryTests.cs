using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Invoices;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenKSeF.Domain.Abstractions;
using OpenKSeF.Domain.Enums;
using OpenKSeF.Sync;

using CirfmfKSeFClient = KSeF.Client.Core.Interfaces.Clients.IKSeFClient;

namespace OpenKSeF.Api.Tests.Services;

public class KSeFGatewayQueryTests
{
    private static readonly KSeFSession Session = new("token", string.Empty, DateTime.UtcNow.AddHours(1));
    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly CirfmfKSeFClient _client = Substitute.For<CirfmfKSeFClient>();
    private InvoiceQueryFilters? _sent;

    private KSeFGateway Gateway(PagedInvoiceResponse response)
    {
        _client.QueryInvoiceMetadataAsync(
                Arg.Do<InvoiceQueryFilters>(f => _sent = f),
                Arg.Any<string>(),
                Arg.Any<int?>(),
                Arg.Any<int?>(),
                Arg.Any<SortOrder>(),
                Arg.Any<CancellationToken>())
            .Returns(response);

        return new KSeFGateway(_client, Substitute.For<IAuthCoordinator>(), Substitute.For<ICryptographyService>(), NullLogger<KSeFGateway>.Instance);
    }

    private static PagedInvoiceResponse Response(params InvoiceSummary[] invoices) => new() { Invoices = invoices.ToList() };

    private static InvoiceSummary Summary(BuyerIdentifierType type, string? value, string? buyerName = "Nabywca", string? sellerName = "Sprzedawca") => new()
    {
        KsefNumber = "KSEF-1",
        InvoiceNumber = "FV 1",
        IssueDate = From,
        AcquisitionDate = From,
        PermanentStorageDate = To.AddDays(-1),
        Seller = new Seller { Nip = "1234563218", Name = sellerName },
        Buyer = new Buyer { Name = buyerName, Identifier = new BuyerIdentifier { Type = type, Value = value } },
        Currency = "GBP"
    };

    private static KSeFQueryCriteria Criteria(InvoiceDirection direction) => new("1234563218", From, To, Direction: direction);

    [Fact]
    public async Task Issued_QueriesSubject1_RestrictedToHwm_OnPermanentStorage()
    {
        await Gateway(Response()).QueryInvoicesAsync(Session, Criteria(InvoiceDirection.Issued));

        Assert.Equal(InvoiceSubjectType.Subject1, _sent!.SubjectType);
        Assert.True(_sent.DateRange.RestrictToPermanentStorageHwmDate);
        Assert.Equal(DateType.PermanentStorage, _sent.DateRange.DateType);
    }

    [Fact]
    public async Task Received_QueryIsUnchanged()
    {
        await Gateway(Response()).QueryInvoicesAsync(Session, Criteria(InvoiceDirection.Received));

        Assert.Equal(InvoiceSubjectType.Subject2, _sent!.SubjectType);
        Assert.Null(_sent.DateRange.RestrictToPermanentStorageHwmDate);
    }

    [Theory]
    [InlineData(BuyerIdentifierType.VatUe, "DE123456789")]
    [InlineData(BuyerIdentifierType.Other, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task NonNipBuyer_GoesToForeignId_NeverToBuyerNip(BuyerIdentifierType type, string value)
    {
        var result = await Gateway(Response(Summary(type, value))).QueryInvoicesAsync(Session, Criteria(InvoiceDirection.Issued));

        var header = result.Invoices.Single();
        Assert.Null(header.BuyerNip);
        Assert.Equal(type.ToString(), header.BuyerIdType);
        Assert.Equal(value, header.BuyerForeignId);
        Assert.Equal("GBP", header.Currency);
    }

    [Fact]
    public async Task NipBuyer_GoesToBuyerNip()
    {
        var result = await Gateway(Response(Summary(BuyerIdentifierType.Nip, "5261040828"))).QueryInvoicesAsync(Session, Criteria(InvoiceDirection.Issued));

        var header = result.Invoices.Single();
        Assert.Equal("5261040828", header.BuyerNip);
        Assert.Equal("Nip", header.BuyerIdType);
        Assert.Null(header.BuyerForeignId);
    }

    [Fact]
    public async Task NoBuyerId_MapsToNullIds()
    {
        var result = await Gateway(Response(Summary(BuyerIdentifierType.None, null))).QueryInvoicesAsync(Session, Criteria(InvoiceDirection.Issued));

        var header = result.Invoices.Single();
        Assert.Null(header.BuyerNip);
        Assert.Equal("None", header.BuyerIdType);
        Assert.Null(header.BuyerForeignId);
    }

    [Fact]
    public async Task Names_AreCutTo500()
    {
        var result = await Gateway(Response(Summary(BuyerIdentifierType.Nip, "5261040828", new string('b', 512), new string('s', 512))))
            .QueryInvoicesAsync(Session, Criteria(InvoiceDirection.Issued));

        var header = result.Invoices.Single();
        Assert.Equal(500, header.BuyerName!.Length);
        Assert.Equal(500, header.VendorName.Length);
    }

    [Fact]
    public async Task Continuation_IsHwm_WhenNotTruncated()
    {
        var hwm = new DateTimeOffset(To.AddMinutes(-2));
        var response = Response(Summary(BuyerIdentifierType.Nip, "5261040828"));
        response.PermanentStorageHwmDate = hwm;

        var result = await Gateway(response).QueryInvoicesAsync(Session, Criteria(InvoiceDirection.Issued));

        Assert.Equal(hwm.UtcDateTime, result.ContinuationAt);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public async Task Continuation_IsLastRow_WhenTruncated()
    {
        var response = Response(Summary(BuyerIdentifierType.Nip, "5261040828"));
        response.PermanentStorageHwmDate = new DateTimeOffset(To);
        response.IsTruncated = true;

        var result = await Gateway(response).QueryInvoicesAsync(Session, Criteria(InvoiceDirection.Issued));

        Assert.Equal(To.AddDays(-1), result.ContinuationAt);
        Assert.True(result.IsTruncated);
    }
}
