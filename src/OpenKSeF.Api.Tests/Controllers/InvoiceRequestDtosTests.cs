using OpenKSeF.Invoices.Contracts.Dtos.Requests;
using OpenKSeF.Invoices.Domain.Enums;
using System.Text.Json;

namespace OpenKSeF.Api.Tests.Controllers;

public class InvoiceRequestDtosTests
{
    [Fact]
    public void CreateInvoiceRequest_ToCommand_MapsRouteAndBodyValues()
    {
        var tenantId = Guid.NewGuid();
        var issueDate = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc);
        var request = new CreateInvoiceRequest(
            DocumentKind.VatInvoice,
            "Seller",
            "1234567890",
            "Buyer",
            BuyerKind.Business,
            "0987654321",
            "PLN",
            issueDate,
            KsefSubmissionRequirement.Required,
            "FV/1/2026",
            "EXT-1");

        var command = request.ToCommand(tenantId);

        Assert.Equal(tenantId, command.TenantId);
        Assert.Equal(DocumentKind.VatInvoice, command.Kind);
        Assert.Equal(BuyerKind.Business, command.BuyerKind);
        Assert.Equal(KsefSubmissionRequirement.Required, command.KsefSubmissionRequirement);
        Assert.Equal("FV/1/2026", command.DocumentNumber);
        Assert.Equal("EXT-1", command.ExternalReference);
    }

    [Fact]
    public void CreateInvoiceRequest_SerializesEnumsAsPascalCaseStrings()
    {
        var request = new CreateInvoiceRequest(
            DocumentKind.CorrectionInvoice,
            "Seller",
            "1234567890",
            "Buyer",
            BuyerKind.Consumer,
            null,
            "PLN",
            new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
            KsefSubmissionRequirement.Optional);

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"Kind\":\"CorrectionInvoice\"", json);
        Assert.Contains("\"BuyerKind\":\"Consumer\"", json);
        Assert.Contains("\"KsefSubmissionRequirement\":\"Optional\"", json);
    }

    [Fact]
    public void CreateInvoiceRequest_RejectsNumericEnumPayloads()
    {
        const string json = """
            {
              "Kind": 0,
              "SellerName": "Seller",
              "SellerNip": "1234567890",
              "BuyerName": "Buyer",
              "BuyerKind": 0,
              "Currency": "PLN",
              "IssueDate": "2026-04-10T00:00:00Z",
              "KsefSubmissionRequirement": 1
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateInvoiceRequest>(json));
    }

    [Fact]
    public void UpdateInvoiceDraftRequest_ToCommand_MapsOptionalPatchValues()
    {
        var invoiceId = Guid.NewGuid();
        var request = new UpdateInvoiceDraftRequest(
            DocumentNumber: "FV/2/2026",
            ExternalReference: "ERP-22",
            Lines:
            [
                new UpdateInvoiceDraftLineRequest(
                    1,
                    "Service",
                    2m,
                    "h",
                    PricingMode.Net,
                    150m,
                    5m,
                    "23%")
            ]);

        var command = request.ToCommand(invoiceId);

        Assert.Equal(invoiceId, command.InvoiceId);
        Assert.Null(command.IssueDate);
        Assert.Equal("FV/2/2026", command.DocumentNumber);
        Assert.Equal("ERP-22", command.ExternalReference);
        var line = Assert.Single(command.Lines!);
        Assert.Equal(1, line.LineNumber);
        Assert.Equal(PricingMode.Net, line.PricingMode);
        Assert.Equal(150m, line.UnitPrice);
        Assert.Equal("23%", line.VatRate);
    }
}
