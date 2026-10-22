using System.Text;

namespace Office.Domain.Common;

/// <summary>
/// A phone number as people write it in the office's files («010 1234 5678», «+20 10…», «٠١٠…»,
/// «0020…») → the WhatsApp id (international, digits only). Egyptian mobile numbers get 20.
/// </summary>
public static class PhoneNumbers
{
    public static bool TryToWaId(string? written, out string waId)
    {
        waId = "";
        if (string.IsNullOrWhiteSpace(written))
        {
            return false;
        }

        var sb = new StringBuilder();
        foreach (var ch in written.Trim())
        {
            if (ch is >= '0' and <= '9')
            {
                sb.Append(ch);
            }
            else if (ch is >= '٠' and <= '٩')
            {
                sb.Append((char)('0' + (ch - '٠')));
            }
            else if (ch is >= '۰' and <= '۹')
            {
                sb.Append((char)('0' + (ch - '۰')));
            }
            else if (ch is not (' ' or '-' or '(' or ')' or '.' or '+' or '‏' or '‎'))
            {
                return false; // letters or other symbols: not a phone number
            }
        }

        var digits = sb.ToString();
        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (digits.Length == 11 && digits.StartsWith("01", StringComparison.Ordinal))
        {
            digits = "2" + digits; // 01xxxxxxxxx → 201xxxxxxxxx
        }
        else if (digits.Length == 10 && digits.StartsWith('1'))
        {
            digits = "20" + digits;
        }

        if (digits.Length is < 8 or > 15 || digits[0] == '0')
        {
            return false;
        }

        waId = digits;
        return true;
    }
}
