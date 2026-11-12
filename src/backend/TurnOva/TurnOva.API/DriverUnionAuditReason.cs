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
        @"(?<![\p{L}\p{N}._%+-])[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.[\p{L}]{2,}(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    private static readonly Regex PhonePattern = new(
        @"(?<![\p{L}\p{N}])\+?[0-9](?:[0-9\s().-]{7,}[0-9])(?![\p{L}\p{N}])",
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

            foreach (Match match in PhonePattern.Matches(reason))
            {
                var digitCount = 0;
                var currentGroupDigits = 0;
                var significantGroups = 0;
                var groupCount = 0;
                var maximumGroupDigits = 0;

                foreach (var character in match.Value)
                {
                    if (char.IsAsciiDigit(character))
                    {
                        digitCount++;
                        currentGroupDigits++;
                        continue;
                    }

                    if (currentGroupDigits == 0)
                        continue;

                    groupCount++;
                    if (currentGroupDigits >= 3)
                        significantGroups++;
                    maximumGroupDigits = Math.Max(maximumGroupDigits, currentGroupDigits);
                    currentGroupDigits = 0;
                }

                if (currentGroupDigits > 0)
                {
                    groupCount++;
                    if (currentGroupDigits >= 3)
                        significantGroups++;
                    maximumGroupDigits = Math.Max(maximumGroupDigits, currentGroupDigits);
                }

                // Keep common ISO date/time text such as 2026-11-13 10:30 valid,
                // while rejecting a long contiguous number or phone-like grouped number.
                if (digitCount >= 10 &&
                    (groupCount == 1 || significantGroups >= 2 || maximumGroupDigits >= 10))
                    return false;
            }

            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            // A bounded validator must fail closed if a future pattern/input regresses.
            return false;
        }
    }
}
