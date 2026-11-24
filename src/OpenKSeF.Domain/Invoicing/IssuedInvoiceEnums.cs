namespace OpenKSeF.Domain.Invoicing;

public enum IssuedInvoiceStatus
{
    Draft,
    Queued,
    Sending,
    Accepted,
    Rejected,
    Issued
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
    KorRoz,
    Pro
}

public enum CorrectionMode
{
    Edit,
    ToZero,
    WrongBuyerId
}

/// <summary>KSeF RodzajFaktury codes, stored and sent on the wire, and the numbering series of each kind. PRO is a non-fiscal proforma, never sent.</summary>
public static class IssuedInvoiceTypes
{
    public const string FvSeries = "FV";
    public const string KorSeries = "KOR";
    public const string ZalSeries = "ZAL";
    public const string ProSeries = "PRO";

    public static string Code(this IssuedInvoiceType type) => type switch
    {
        IssuedInvoiceType.Vat => "VAT",
        IssuedInvoiceType.Kor => "KOR",
        IssuedInvoiceType.Zal => "ZAL",
        IssuedInvoiceType.Roz => "ROZ",
        IssuedInvoiceType.KorZal => "KOR_ZAL",
        IssuedInvoiceType.KorRoz => "KOR_ROZ",
        IssuedInvoiceType.Pro => "PRO",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static IssuedInvoiceType Parse(string code) => code switch
    {
        "VAT" => IssuedInvoiceType.Vat,
        "KOR" => IssuedInvoiceType.Kor,
        "ZAL" => IssuedInvoiceType.Zal,
        "ROZ" => IssuedInvoiceType.Roz,
        "KOR_ZAL" => IssuedInvoiceType.KorZal,
        "KOR_ROZ" => IssuedInvoiceType.KorRoz,
        "PRO" => IssuedInvoiceType.Pro,
        _ => throw new FormatException($"Unknown invoice type '{code}'.")
    };

    public static bool IsCorrection(this IssuedInvoiceType type) => type is IssuedInvoiceType.Kor or IssuedInvoiceType.KorZal or IssuedInvoiceType.KorRoz;

    public static bool IsAdvance(this IssuedInvoiceType type) => type is IssuedInvoiceType.Zal or IssuedInvoiceType.KorZal;

    public static IssuedInvoiceType CorrectionOf(this IssuedInvoiceType type) => type switch
    {
        IssuedInvoiceType.Vat or IssuedInvoiceType.Kor => IssuedInvoiceType.Kor,
        IssuedInvoiceType.Zal or IssuedInvoiceType.KorZal => IssuedInvoiceType.KorZal,
        IssuedInvoiceType.Roz or IssuedInvoiceType.KorRoz => IssuedInvoiceType.KorRoz,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static string Series(this IssuedInvoiceType type) => type switch
    {
        IssuedInvoiceType.Vat or IssuedInvoiceType.Roz => FvSeries,
        IssuedInvoiceType.Kor or IssuedInvoiceType.KorZal or IssuedInvoiceType.KorRoz => KorSeries,
        IssuedInvoiceType.Zal => ZalSeries,
        IssuedInvoiceType.Pro => ProSeries,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public static IssuedInvoiceType[] TypesOf(string series) => series switch
    {
        FvSeries => [IssuedInvoiceType.Vat, IssuedInvoiceType.Roz],
        KorSeries => [IssuedInvoiceType.Kor, IssuedInvoiceType.KorZal, IssuedInvoiceType.KorRoz],
        ZalSeries => [IssuedInvoiceType.Zal],
        ProSeries => [IssuedInvoiceType.Pro],
        _ => throw new ArgumentOutOfRangeException(nameof(series), series, null)
    };

    public static string Pattern(this SellerProfile profile, IssuedInvoiceType type) => type.Series() switch
    {
        FvSeries => profile.NumberingPattern,
        KorSeries => profile.CorrectionNumberingPattern,
        ZalSeries => profile.AdvanceNumberingPattern,
        ProSeries => profile.ProformaNumberingPattern,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };
}
