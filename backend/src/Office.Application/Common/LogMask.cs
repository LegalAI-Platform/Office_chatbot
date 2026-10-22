namespace Office.Application.Common;

public static class LogMask
{
    /// <summary>Keeps only the last 3 digits of a phone number, for logs.</summary>
    public static string Phone(string? waId) =>
        string.IsNullOrEmpty(waId) || waId.Length <= 3 ? "***" : new string('*', waId.Length - 3) + waId[^3..];
}
