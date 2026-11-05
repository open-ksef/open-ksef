using System.Text.Json;
using System.Text.Json.Nodes;
using OpenKSeF.Api.Models;

namespace OpenKSeF.Api.Tests.Models;

public class TransferContractSerializationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TransferDetailsResponse_NoQr_WritesNullQrAndMessage()
    {
        var response = new TransferDetailsResponse(
            "Odbiorca 1", null, "1234567890", 100m, "PLN", "FV 1/2026", "text", null, "Brak kwoty do zapłaty.");

        var json = JsonNode.Parse(JsonSerializer.Serialize(response, Web))!.AsObject();

        Assert.True(json.ContainsKey("qrCodeBase64"));
        Assert.Null(json["qrCodeBase64"]);
        Assert.Equal("Brak kwoty do zapłaty.", json["qrMessage"]!.GetValue<string>());
    }

    [Fact]
    public void TransferDetailsResponse_WithQr_WritesNullMessage()
    {
        var response = new TransferDetailsResponse(
            "Odbiorca 1", "61109010140000071219812874", "1234567890", 100m, "PLN", "FV 1/2026", "text", "data:image/png;base64,AA");

        var json = JsonNode.Parse(JsonSerializer.Serialize(response, Web))!.AsObject();

        Assert.Equal("data:image/png;base64,AA", json["qrCodeBase64"]!.GetValue<string>());
        Assert.True(json.ContainsKey("qrMessage"));
        Assert.Null(json["qrMessage"]);
    }

    [Theory]
    [InlineData("61109010140000071219812874")]
    [InlineData(null)]
    public void InvoiceResponse_WritesVendorBankAccount(string? account)
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var response = new InvoiceResponse(
            Guid.Empty, "K", "R", "FV 1/2026", "Odbiorca 1", "1234567890", null, null,
            81.30m, 18.70m, 100m, "PLN", now, null, "Vat", now, false, null, account);

        var json = JsonNode.Parse(JsonSerializer.Serialize(response, Web))!.AsObject();

        Assert.True(json.ContainsKey("vendorBankAccount"));
        Assert.Equal(account, json["vendorBankAccount"]?.GetValue<string>());
    }
}
