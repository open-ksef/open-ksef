namespace OpenKSeF.Domain.Invoicing;

public enum IssuedInvoiceStatus
{
    Draft,
    Queued,
    Sending,
    Accepted,
    Rejected
}

public enum BuyerKind
{
    Company,
    Person,
    Foreign
}

public enum PaymentMethod
{
    Transfer,
    Cash,
    Card
}

public enum ExemptionBasisKind
{
    Act,
    Directive,
    Other
}

public enum IssuedInvoiceType
{
    Vat,
    Kor
}

public enum CorrectionMode
{
    Edit,
    ToZero,
    WrongBuyerId
}

/// <summary>KSeF RodzajFaktury codes, stored and sent on the wire, and the numbering series of each kind.</summary>
public static class IssuedInvoiceTypes
{
    public const string FvSeries = "FV";
    public const string KorSeries = "KOR";

    public static string Code(this IssuedInvoiceType type) => type switch
    {
        IssuedInvoiceType.Kor => "KOR",
        _ => "VAT"
    };

    public static IssuedInvoiceType Parse(string code) => code switch
    {
        "VAT" => IssuedInvoiceType.Vat,
        "KOR" => IssuedInvoiceType.Kor,
        _ => throw new FormatException($"Unknown invoice type '{code}'.")
    };

    public static string Series(this IssuedInvoiceType type) => type == IssuedInvoiceType.Kor ? KorSeries : FvSeries;

    public static string Pattern(this SellerProfile profile, IssuedInvoiceType type) =>
        type == IssuedInvoiceType.Kor ? profile.CorrectionNumberingPattern : profile.NumberingPattern;
}
