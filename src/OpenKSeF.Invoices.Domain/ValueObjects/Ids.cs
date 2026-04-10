namespace OpenKSeF.Invoices.Domain.ValueObjects;

public sealed record InvoiceId(Guid Value)
{
    public static InvoiceId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public sealed record TenantId(Guid Value)
{
    public override string ToString() => Value.ToString();
}

public sealed record LineId(Guid Value)
{
    public static LineId New() => new(Guid.NewGuid());
}
