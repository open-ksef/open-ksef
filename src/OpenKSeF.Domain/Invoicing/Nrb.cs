namespace OpenKSeF.Domain.Invoicing;

public static class Nrb
{
    public static string Normalize(string value) =>
        new(value.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    public static bool IsValid(string? digits)
    {
        if (digits is not { Length: 26 } || !digits.All(char.IsAsciiDigit))
            return false;

        var rearranged = digits[2..] + "2521" + digits[..2];
        var remainder = 0;
        foreach (var c in rearranged)
            remainder = (remainder * 10 + (c - '0')) % 97;
        return remainder == 1;
    }

    public static string NormalizeIban(string value) => Normalize(value).ToUpperInvariant();

    public static bool IsValidIban(string? iban)
    {
        if (iban is not { Length: >= 15 and <= 34 }
            || !char.IsAsciiLetterUpper(iban[0]) || !char.IsAsciiLetterUpper(iban[1])
            || !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3])
            || !iban.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)))
            return false;

        var remainder = 0;
        foreach (var c in iban[4..] + iban[..4])
            remainder = char.IsAsciiDigit(c)
                ? (remainder * 10 + (c - '0')) % 97
                : (remainder * 100 + (c - 'A' + 10)) % 97;
        return remainder == 1;
    }
}
