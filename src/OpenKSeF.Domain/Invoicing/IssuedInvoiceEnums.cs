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
    Kor,
    Zal,
    Roz,
    KorZal,
    KorRoz
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
    public const string ZalSeries = "ZAL";

    public static string Code(this IssuedInvoiceType type) => type switch
    {
        IssuedInvoiceType.Kor => "KOR",
        IssuedInvoiceType.Zal => "ZAL",
        IssuedInvoiceType.Roz => "ROZ",
        IssuedInvoiceType.KorZal => "KOR_ZAL",
        IssuedInvoiceType.KorRoz => "KOR_ROZ",
        _ => "VAT"
    };

    public static IssuedInvoiceType Parse(string code) => code switch
    {
        "VAT" => IssuedInvoiceType.Vat,
        "KOR" => IssuedInvoiceType.Kor,
        "ZAL" => IssuedInvoiceType.Zal,
        "ROZ" => IssuedInvoiceType.Roz,
        "KOR_ZAL" => IssuedInvoiceType.KorZal,
        "KOR_ROZ" => IssuedInvoiceType.KorRoz,
        _ => throw new FormatException($"Unknown invoice type '{code}'.")
    };

    public static bool IsCorrection(this IssuedInvoiceType type) => type is IssuedInvoiceType.Kor or IssuedInvoiceType.KorZal or IssuedInvoiceType.KorRoz;

    public static bool IsAdvance(this IssuedInvoiceType type) => type is IssuedInvoiceType.Zal or IssuedInvoiceType.KorZal;

    public static IssuedInvoiceType CorrectionOf(this IssuedInvoiceType type) => type switch
    {
        IssuedInvoiceType.Zal => IssuedInvoiceType.KorZal,
        IssuedInvoiceType.Roz => IssuedInvoiceType.KorRoz,
        _ => IssuedInvoiceType.Kor
    };

    public static string Series(this IssuedInvoiceType type) => type switch
    {
        _ when type.IsCorrection() => KorSeries,
        IssuedInvoiceType.Zal => ZalSeries,
        _ => FvSeries
    };

    public static IssuedInvoiceType[] TypesOf(string series) => series switch
    {
        KorSeries => [IssuedInvoiceType.Kor, IssuedInvoiceType.KorZal, IssuedInvoiceType.KorRoz],
        ZalSeries => [IssuedInvoiceType.Zal],
        _ => [IssuedInvoiceType.Vat, IssuedInvoiceType.Roz]
    };

    public static string Pattern(this SellerProfile profile, IssuedInvoiceType type) => type.Series() switch
    {
        KorSeries => profile.CorrectionNumberingPattern,
        ZalSeries => profile.AdvanceNumberingPattern,
        _ => profile.NumberingPattern
    };
}
