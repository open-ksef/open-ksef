namespace OpenKSeF.Domain.Invoicing;

/// <summary>Sales invoice issued by a tenant: editable draft until issued, then frozen with a seller snapshot and number.</summary>
public sealed class IssuedInvoice
{
    public const string Currency = "PLN";
    public const int DefaultPaymentDays = 14;

    private static readonly TimeZoneInfo PolandTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    public static DateOnly PolandDate(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, PolandTimeZone).DateTime);

    private readonly List<IssuedInvoiceLine> _lines = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public IssuedInvoiceStatus Status { get; private set; }
    public string? Number { get; private set; }

    public string? SellerNip { get; private set; }
    public string? SellerName { get; private set; }
    public string? SellerAddressLine1 { get; private set; }
    public string? SellerAddressLine2 { get; private set; }
    public string? SellerBankAccount { get; private set; }
    public bool SellerCashAccounting { get; private set; }

    public BuyerKind BuyerKind { get; private set; }
    public string? BuyerNip { get; private set; }
    public string? BuyerName { get; private set; }
    public string? BuyerAddressLine1 { get; private set; }
    public string? BuyerAddressLine2 { get; private set; }

    public DateOnly IssueDate { get; private set; }
    public DateOnly SaleDate { get; private set; }
    public DateOnly DueDate { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }
    public bool SplitPayment { get; private set; }
    public ExemptionBasisKind? ExemptionBasisKind { get; private set; }
    public string? ExemptionBasisText { get; private set; }

    public decimal TotalNet { get; private set; }
    public decimal TotalVat { get; private set; }
    public decimal TotalGross { get; private set; }

    public string? KsefEnvironment { get; private set; }
    public byte[]? SentXml { get; private set; }
    public string? SentXmlSha256 { get; private set; }
    public string? VerificationUrl { get; private set; }
    public string? KsefSessionRef { get; private set; }
    public string? KsefElementRef { get; private set; }
    public DateTime? SendStartedAt { get; private set; }
    public string? KsefNumber { get; private set; }
    public int? KsefStatusCode { get; private set; }
    public string? KsefStatusDescription { get; private set; }
    public string? KsefStatusDetails { get; private set; }
    public byte[]? UpoXml { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? IssuedAt { get; private set; }
    public DateTime? AcceptedAt { get; private set; }

    public IReadOnlyList<IssuedInvoiceLine> Lines => _lines;

    public bool IsEditable => Status is IssuedInvoiceStatus.Draft or IssuedInvoiceStatus.Rejected;

    private IssuedInvoice() { }

    public static IssuedInvoice Draft(Guid id, Guid tenantId, DateOnly today, DateTime now) => new()
    {
        Id = id,
        TenantId = tenantId,
        Status = IssuedInvoiceStatus.Draft,
        BuyerKind = BuyerKind.Company,
        IssueDate = today,
        SaleDate = today,
        DueDate = today.AddDays(DefaultPaymentDays),
        PaymentMethod = PaymentMethod.Transfer,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void SetBuyer(BuyerKind kind, string? nip, string? name, string? addressLine1, string? addressLine2)
    {
        EnsureEditable();
        BuyerKind = kind;
        BuyerNip = Clean(nip)?.Replace("-", "");
        BuyerName = Clean(name);
        BuyerAddressLine1 = Clean(addressLine1);
        BuyerAddressLine2 = Clean(addressLine2);
    }

    public void SetDates(DateOnly issueDate, DateOnly saleDate, DateOnly dueDate)
    {
        EnsureEditable();
        IssueDate = issueDate;
        SaleDate = saleDate;
        DueDate = dueDate;
    }

    public void SetPayment(PaymentMethod method, bool splitPayment)
    {
        EnsureEditable();
        PaymentMethod = method;
        SplitPayment = splitPayment;
    }

    public void SetExemptionBasis(ExemptionBasisKind? kind, string? text)
    {
        EnsureEditable();
        ExemptionBasisKind = kind;
        ExemptionBasisText = kind is null ? null : Clean(text);
    }

    public void AddLine(string? name, string? unit, decimal quantity, decimal unitNetPrice, VatRate vatRate)
    {
        EnsureEditable();
        _lines.Add(new IssuedInvoiceLine(Id, _lines.Count + 1, name, unit, quantity, unitNetPrice, vatRate));
        RecalculateTotals();
    }

    public void ReplaceLines(IEnumerable<(string? Name, string? Unit, decimal Quantity, decimal UnitNetPrice, VatRate VatRate)> lines)
    {
        EnsureEditable();
        _lines.Clear();
        foreach (var line in lines)
            _lines.Add(new IssuedInvoiceLine(Id, _lines.Count + 1, line.Name, line.Unit, line.Quantity, line.UnitNetPrice, line.VatRate));
        RecalculateTotals();
    }

    public void Touch(DateTime now) => UpdatedAt = now;

    public IReadOnlyList<VatSummary> VatSummary() => _lines
        .GroupBy(l => l.VatRate)
        .OrderBy(g => g.Key)
        .Select(g =>
        {
            var net = g.Sum(l => l.NetAmount);
            return new VatSummary(g.Key, net, Math.Round(net * g.Key.Fraction(), 2, MidpointRounding.AwayFromZero));
        })
        .ToList();

    public void Issue(SellerProfile profile, string sellerNip, string? number, DateTime now)
    {
        EnsureEditable();

        var errors = IssueChecks.Errors(this, profile);
        if (errors.Count > 0)
            throw new InvoiceValidationException(errors);

        var assigned = Number ?? number;
        if (string.IsNullOrWhiteSpace(assigned))
            throw new InvoiceStateException("Invoice number is required to issue a draft.");

        SellerNip = sellerNip;
        SellerName = profile.LegalName;
        SellerAddressLine1 = profile.AddressLine1;
        SellerAddressLine2 = profile.AddressLine2;
        SellerBankAccount = profile.BankAccount;
        SellerCashAccounting = profile.CashAccounting;
        Number = assigned;
        Status = IssuedInvoiceStatus.Queued;
        SentXml = null;
        SentXmlSha256 = null;
        VerificationUrl = null;
        KsefSessionRef = null;
        KsefElementRef = null;
        SendStartedAt = null;
        KsefStatusCode = null;
        KsefStatusDescription = null;
        KsefStatusDetails = null;
        AttemptCount = 0;
        LastError = null;
        NextAttemptAt = now;
        IssuedAt = now;
        UpdatedAt = now;
    }

    public void AttachSentXml(byte[] xml, string sha256, string ksefEnvironment, string? verificationUrl)
    {
        EnsureStatus(IssuedInvoiceStatus.Queued);
        SentXml = xml;
        SentXmlSha256 = sha256;
        KsefEnvironment = ksefEnvironment;
        VerificationUrl = verificationUrl;
    }

    public void MarkSendStarted(string sessionRef, DateTime now)
    {
        EnsureStatus(IssuedInvoiceStatus.Queued, IssuedInvoiceStatus.Sending);
        KsefSessionRef = sessionRef;
        SendStartedAt = now;
        AttemptCount++;
        Status = IssuedInvoiceStatus.Sending;
        UpdatedAt = now;
    }

    public void RecordElementRef(string elementRef, DateTime now)
    {
        EnsureStatus(IssuedInvoiceStatus.Sending);
        KsefElementRef = elementRef;
        UpdatedAt = now;
    }

    public void MarkAccepted(string ksefNumber, DateTime acceptedAt, int statusCode, string? statusDescription, DateTime now)
    {
        EnsureStatus(IssuedInvoiceStatus.Queued, IssuedInvoiceStatus.Sending);
        KsefNumber = ksefNumber;
        AcceptedAt = acceptedAt;
        KsefStatusCode = statusCode;
        KsefStatusDescription = statusDescription;
        LastError = null;
        NextAttemptAt = now;
        Status = IssuedInvoiceStatus.Accepted;
        UpdatedAt = now;
    }

    public void AttachUpo(byte[] upoXml, DateTime now)
    {
        EnsureStatus(IssuedInvoiceStatus.Accepted);
        UpoXml = upoXml;
        NextAttemptAt = null;
        LastError = null;
        UpdatedAt = now;
    }

    public void MarkRejected(int? statusCode, string statusDescription, string? statusDetails, DateTime now)
    {
        EnsureStatus(IssuedInvoiceStatus.Queued, IssuedInvoiceStatus.Sending);
        KsefStatusCode = statusCode;
        KsefStatusDescription = statusDescription;
        KsefStatusDetails = statusDetails;
        NextAttemptAt = null;
        Status = IssuedInvoiceStatus.Rejected;
        UpdatedAt = now;
    }

    public void ScheduleRetry(string error, DateTime nextAttemptAt, DateTime now)
    {
        EnsureStatus(IssuedInvoiceStatus.Queued, IssuedInvoiceStatus.Sending, IssuedInvoiceStatus.Accepted);
        LastError = error;
        NextAttemptAt = nextAttemptAt;
        UpdatedAt = now;
    }

    private void EnsureStatus(params IssuedInvoiceStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new InvoiceStateException($"Invoice in status {Status} cannot do this; expected {string.Join(" or ", allowed)}.");
    }

    private void RecalculateTotals()
    {
        var summary = VatSummary();
        TotalNet = summary.Sum(s => s.Net);
        TotalVat = summary.Sum(s => s.Vat);
        TotalGross = TotalNet + TotalVat;
    }

    private void EnsureEditable()
    {
        if (!IsEditable)
            throw new InvoiceStateException($"Invoice in status {Status} cannot be changed.");
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record VatSummary(VatRate VatRate, decimal Net, decimal Vat);
