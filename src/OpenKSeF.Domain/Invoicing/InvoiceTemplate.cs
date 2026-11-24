namespace OpenKSeF.Domain.Invoicing;

/// <summary>Named snapshot of a VAT invoice that new drafts start from (REQ-0034 rule 6); copy and reissue use an unsaved one. Content never changes after capture.</summary>
public sealed class InvoiceTemplate
{
    public const int MaxNameLength = 100;

    private readonly List<InvoiceTemplateLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public BuyerKind BuyerKind { get; private set; }
    public string? BuyerNip { get; private set; }
    public string? BuyerName { get; private set; }
    public string? BuyerAddressLine1 { get; private set; }
    public string? BuyerAddressLine2 { get; private set; }
    public string? BuyerCountryCode { get; private set; }
    public string? BuyerIdType { get; private set; }
    public string? BuyerForeignId { get; private set; }

    public string Currency { get; private set; } = IssuedInvoice.DefaultCurrency;
    public PaymentMethod PaymentMethod { get; private set; }
    public int PaymentDays { get; private set; }
    public bool SplitPayment { get; private set; }
    public ExemptionBasisKind? ExemptionBasisKind { get; private set; }
    public string? ExemptionBasisText { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyList<InvoiceTemplateLine> Lines => _lines;

    private InvoiceTemplate() { }

    /// <summary>Buyer and lines from the current state, the rest and the payment term from the source.</summary>
    public static InvoiceTemplate Capture(Guid tenantId, IssuedInvoice source, IssuedInvoice state, DateTime now, string name = "", bool clearBuyerId = false)
    {
        var template = new InvoiceTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            BuyerKind = state.BuyerKind,
            BuyerNip = clearBuyerId ? null : state.BuyerNip,
            BuyerName = state.BuyerName,
            BuyerAddressLine1 = state.BuyerAddressLine1,
            BuyerAddressLine2 = state.BuyerAddressLine2,
            BuyerCountryCode = state.BuyerCountryCode,
            BuyerIdType = clearBuyerId && state.BuyerKind == BuyerKind.Foreign ? IssuedInvoice.BuyerIdNone : state.BuyerIdType,
            BuyerForeignId = clearBuyerId ? null : state.BuyerForeignId,
            Currency = source.Currency,
            PaymentMethod = source.PaymentMethod,
            PaymentDays = Math.Max(0, source.DueDate.DayNumber - source.IssueDate.DayNumber),
            SplitPayment = source.SplitPayment,
            ExemptionBasisKind = source.ExemptionBasisKind,
            ExemptionBasisText = source.ExemptionBasisText,
            CreatedAt = now,
            UpdatedAt = now
        };
        template._lines.AddRange(state.Lines.OrderBy(l => l.LineNo)
            .Select((l, i) => new InvoiceTemplateLine(template.Id, i + 1, l.Name, l.Unit, l.Quantity, l.UnitNetPrice, l.VatRate)));
        return template;
    }

    public void Rename(string name, DateTime now)
    {
        Name = name;
        UpdatedAt = now;
    }
}

public sealed class InvoiceTemplateLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid InvoiceTemplateId { get; private set; }
    public int LineNo { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Unit { get; private set; } = IssuedInvoiceLine.DefaultUnit;
    public decimal Quantity { get; private set; }
    public decimal UnitNetPrice { get; private set; }
    public VatRate VatRate { get; private set; }

    private InvoiceTemplateLine() { }

    internal InvoiceTemplateLine(Guid invoiceTemplateId, int lineNo, string name, string unit, decimal quantity, decimal unitNetPrice, VatRate vatRate)
    {
        InvoiceTemplateId = invoiceTemplateId;
        LineNo = lineNo;
        Name = name;
        Unit = unit;
        Quantity = quantity;
        UnitNetPrice = unitNetPrice;
        VatRate = vatRate;
    }
}
