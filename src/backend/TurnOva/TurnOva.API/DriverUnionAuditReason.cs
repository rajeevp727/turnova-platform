using System.Text.RegularExpressions;

namespace TurnOva.API;

/// <summary>
/// Conservative validation for free-text values that are persisted into Union audit facts.
/// This rejects obvious email addresses and phone-like sequences; it is not a general PII scanner.
/// </summary>
public static class DriverUnionAuditReason
{
    private const int MaximumLength = 300;

    private static readonly Regex EmailPattern = new(
        @"(?<![\\p{L}\\p{N}._%+-])[\\p{L}\\p{N}._%+-]+@[\\p{L}\\p{N}.-]+\\.[\\p{L}]{2,}(?![\\p{L}\\p{N}.-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    private static readonly Regex PhonePattern = new(
        @"(?<![\\p{L}\\p{N}])\\+?[0-9](?:[0-9\\s().-]{7,}[0-9])(?![\\p{L}\\p{N}])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    public static string? Normalize(string? reason)
    {
        var normalized = reason?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > MaximumLength ||
            !IsSafe(normalized)
                ? null
                : normalized;
    }

    public static bool IsNormalizedSafe(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        reason.Length <= MaximumLength &&
        string.Equals(reason, reason.Trim(), StringComparison.Ordinal) &&
        IsSafe(reason);

    public static bool IsSafe(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return false;

        try
        {
            if (EmailPattern.IsMatch(reason))
                return false;

            return !PhonePattern.Matches(reason)
                .Cast<Match>()
                .Any(match => match.Value.Count(char.IsAsciiDigit) >= 10);
        }
        catch (RegexMatchTimeoutException)
        {
            // A bounded validator must fail closed if a future pattern/input regresses.
            return false;
        }
    }
}
