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
}
