using System.Text.Json;
using System.Text.Json.Nodes;
using OpenKSeF.Api.Models;

namespace OpenKSeF.Api.Tests.Models;

public class IssuingContractSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static void AssertJson(object value, string expected) =>
        Assert.Equal(JsonNode.Parse(expected)!.ToJsonString(), JsonSerializer.Serialize(value, value.GetType(), Web));

    [Fact]
    public void SellerProfileResponse_MatchesContract()
    {
        var response = new SellerProfileResponse(
            "OpenKSeF Test A sp. z o.o.", "ul. Prosta 1", "00-001 Warszawa", "61109010140000071219812874", false,
            "FV/{nr}/{MM}/{RRRR}", 57, "2026-11", "1234567890", true, "FV/57/11/2026", "test");

        AssertJson(response, """
            { "legalName": "OpenKSeF Test A sp. z o.o.", "addressLine1": "ul. Prosta 1", "addressLine2": "00-001 Warszawa",
              "bankAccount": "61109010140000071219812874", "cashAccounting": false,
              "numberingPattern": "FV/{nr}/{MM}/{RRRR}", "nextNumber": 57, "nextNumberPeriod": "2026-11",
              "nip": "1234567890", "isComplete": true, "nextNumberPreview": "FV/57/11/2026", "ksefEnvironment": "test" }
            """);
    }

    [Fact]
    public void SellerProfileRequest_DeserializesWithoutOptionalNumbering()
    {
        var request = JsonSerializer.Deserialize<SellerProfileRequest>("""
            { "legalName": "A", "addressLine1": "L1", "addressLine2": "L2", "bankAccount": "61109010140000071219812874",
              "cashAccounting": true, "numberingPattern": "{nr}/{RRRR}" }
            """, Web)!;

        Assert.True(request.CashAccounting);
        Assert.Null(request.NextNumber);
        Assert.Null(request.NextNumberPeriod);
    }

    [Fact]
    public void NipLookupResponse_MatchesContract()
    {
        var response = new NipLookupResponse(
            "1234567890", "Firma", "ul. Prosta 1", "00-001 Warszawa", "Czynny", ["61109010140000071219812874"]);

        AssertJson(response, """
            { "nip": "1234567890", "name": "Firma", "addressLine1": "ul. Prosta 1", "addressLine2": "00-001 Warszawa",
              "vatStatus": "Czynny", "bankAccounts": ["61109010140000071219812874"] }
            """);
    }

    [Fact]
    public void IssuedInvoiceDraftRequest_DeserializesContractSampleAndIgnoresSellerFields()
    {
        var request = JsonSerializer.Deserialize<IssuedInvoiceDraftRequest>("""
            { "seller": { "nip": "9999999999", "name": "Injected" },
              "buyer": { "kind": "Company", "nip": "1234567890", "name": "Nabywca", "addressLine1": "L1", "addressLine2": "L2" },
              "issueDate": "2026-11-03", "saleDate": "2026-11-03", "dueDate": "2026-11-17",
              "paymentMethod": "Transfer", "splitPayment": false,
              "exemptionBasis": { "kind": "Act", "text": "art. 113 ust. 1 ustawy o VAT" },
              "lines": [ { "name": "Usługa", "unit": "szt.", "quantity": 1.5, "unitNetPrice": 100.00, "vatRate": "0 KR" } ] }
            """, Web)!;

        Assert.Equal("Company", request.Buyer!.Kind);
        Assert.Equal(new DateOnly(2026, 11, 3), request.IssueDate);
        Assert.Equal(new DateOnly(2026, 11, 17), request.DueDate);
        Assert.Equal("Act", request.ExemptionBasis!.Kind);
        var line = Assert.Single(request.Lines!);
        Assert.Equal(1.5m, line.Quantity);
        Assert.Equal("0 KR", line.VatRate);
    }

    [Fact]
    public void IssuedInvoiceResponse_MatchesContract()
    {
        var created = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);
        var response = new IssuedInvoiceResponse(
            Guid.Parse("8f1c2a8e-6a5d-4c55-9c7a-1b2c3d4e5f60"), "Draft", null,
            new IssuedInvoiceSellerResponse("1234567890", "S", "SL1", "SL2", "61109010140000071219812874", false),
            new IssuedInvoiceBuyerModel("Company", "1234567890", "B", "BL1", "BL2"),
            new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 17),
            "Transfer", false, null,
            [new IssuedInvoiceLineResponse(1, "Usługa", "szt.", 1m, 100.00m, "23", 100.00m, 23.00m)],
            [new VatSummaryResponse("23", 100.00m, 23.00m)],
            100.00m, 23.00m, 123.00m, "PLN",
            new IssuedInvoiceKsefResponse("test", null, null, null, null, null, null, false),
            [new ValidationIssue("INV-W-MPP", "splitPayment", "Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15)")],
            created, created, null);

        AssertJson(response, """
            { "id": "8f1c2a8e-6a5d-4c55-9c7a-1b2c3d4e5f60", "status": "Draft", "number": null,
              "seller": { "nip": "1234567890", "name": "S", "addressLine1": "SL1", "addressLine2": "SL2", "bankAccount": "61109010140000071219812874", "cashAccounting": false },
              "buyer": { "kind": "Company", "nip": "1234567890", "name": "B", "addressLine1": "BL1", "addressLine2": "BL2" },
              "issueDate": "2026-11-03", "saleDate": "2026-11-03", "dueDate": "2026-11-17",
              "paymentMethod": "Transfer", "splitPayment": false, "exemptionBasis": null,
              "lines": [ { "lineNo": 1, "name": "Usługa", "unit": "szt.", "quantity": 1, "unitNetPrice": 100.00, "vatRate": "23", "netAmount": 100.00, "vatAmount": 23.00 } ],
              "vatSummary": [ { "vatRate": "23", "net": 100.00, "vat": 23.00 } ],
              "totalNet": 100.00, "totalVat": 23.00, "totalGross": 123.00, "currency": "PLN",
              "ksef": { "environment": "test", "ksefNumber": null, "acceptedAt": null, "statusCode": null, "statusMessage": null,
                        "lastError": null, "verificationUrl": null, "hasUpo": false },
              "warnings": [ { "code": "INV-W-MPP", "field": "splitPayment", "message": "Sprawdź, czy faktura wymaga mechanizmu podzielonej płatności (załącznik nr 15)" } ],
              "createdAt": "2026-11-03T10:00:00Z", "updatedAt": "2026-11-03T10:00:00Z", "issuedAt": null }
            """);
    }

    [Fact]
    public void IssuedInvoiceListItemResponse_MatchesContract()
    {
        var response = new IssuedInvoiceListItemResponse(
            Guid.Parse("8f1c2a8e-6a5d-4c55-9c7a-1b2c3d4e5f60"), "Accepted", "FV/1/11/2026", "B", "1234567890",
            new DateOnly(2026, 11, 3), 123.00m, "PLN", "1234567890-20261103-0100A0B0C0D0-E1");

        AssertJson(response, """
            { "id": "8f1c2a8e-6a5d-4c55-9c7a-1b2c3d4e5f60", "status": "Accepted", "number": "FV/1/11/2026", "buyerName": "B", "buyerNip": "1234567890",
              "issueDate": "2026-11-03", "totalGross": 123.00, "currency": "PLN", "ksefNumber": "1234567890-20261103-0100A0B0C0D0-E1" }
            """);
    }

    [Fact]
    public void ValidationEnvelope_MatchesContract()
    {
        var envelope = new ValidationEnvelope(
            [new ValidationIssue("INV-VAL-013", "buyer.nip", "Dla nabywcy firmy wymagany jest poprawny NIP")], []);

        AssertJson(envelope, """
            { "errors": [ { "code": "INV-VAL-013", "field": "buyer.nip", "message": "Dla nabywcy firmy wymagany jest poprawny NIP" } ], "warnings": [] }
            """);
    }
}
