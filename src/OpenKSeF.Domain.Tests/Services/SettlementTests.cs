using OpenKSeF.Domain.Enums;

namespace OpenKSeF.Domain.Tests.Services;

public class SettlementTests
{
    public static TheoryData<string?, string[], string?, string[], string?, string?, string?, SettlementState, decimal?> Cases => new()
    {
        { "1000.00", [], null, [], null, null, null, SettlementState.None, null },
        { "1000.00", [], null, [], "200.00", "800.00", null, SettlementState.Confirmed, 800m },
        { "1000.00", [], "50.00", [], null, "1050.00", null, SettlementState.Confirmed, 1050m },
        { "1000.00", [], null, ["100.00", "50.00"], null, "850.00", null, SettlementState.Confirmed, 850m },
        { "1000.00", ["10.00"], "50.00", [], null, "1050.00", null, SettlementState.Confirmed, 1050m },
        { "1000.00", [], null, [], null, null, "300.00", SettlementState.Overpaid, 300m },
        { "1000.00", [], null, [], "200.00", "800.00", "5.00", SettlementState.Overpaid, 5m },
        { "1000.00", [], null, [], "1000.00", "0.00", null, SettlementState.NothingDue, 0m },
        { "1000.00", [], null, [], null, "-10.00", null, SettlementState.NothingDue, -10m },
        { "1000.00", [], null, [], null, "800.00", null, SettlementState.Unconfirmed, 800m },
        { "1000.001", [], null, [], null, "1000.00", null, SettlementState.Unconfirmed, 1000m },
        { "1000.00", [], null, ["100.00"], null, null, null, SettlementState.Unconfirmed, null },
        { "1000.00", [], "50.00", [], null, null, null, SettlementState.Unconfirmed, null },
        { "1000.00", [], null, ["abc"], null, "900.00", null, SettlementState.Unconfirmed, 900m },
        { "1000.00", [], null, [], "1,00", "999.00", null, SettlementState.Unconfirmed, 999m },
        { "1000.00", [], null, [], null, "x", null, SettlementState.Unconfirmed, null },
        { "1000.00", [], null, [], null, null, "", SettlementState.Unconfirmed, null },
        { null, [], null, [], null, "800.00", null, SettlementState.Unconfirmed, 800m },
        { "1000.00", [], null, [], null, "1000", null, SettlementState.Confirmed, 1000m },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classify(string? p15, string[] charges, string? chargesSum, string[] deductions, string? deductionsSum,
        string? amountDue, string? overpaid, SettlementState state, decimal? amount)
    {
        var result = Settlement.Classify(p15, charges, chargesSum, deductions, deductionsSum, amountDue, overpaid);

        Assert.Equal(state, result.State);
        Assert.Equal(amount, result.Amount);
    }
}
