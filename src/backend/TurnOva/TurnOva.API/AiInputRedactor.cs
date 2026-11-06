using System.Text.RegularExpressions;

namespace TurnOva.API;

/// <summary>
/// Removes route and sensitive identifiers before any optional provider call.
/// The API keeps the original text only for local, deterministic draft parsing.
/// </summary>
public static class AiInputRedactor
{
    private static readonly Regex FromToRoute = new(
        @"\bfrom\s+.+?\s+to\s+.+?(?=\s+(?:for|with)\s+\d+\s+(?:passengers?|people|persons?)\b|[.!?]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PickupDropoffRoute = new(
        @"\bpickup(?:\s+at)?\s+.+?\s+drop(?:-?off)?(?:\s+at)?\s+.+?(?=\s+(?:for|with)\s+\d+\s+(?:passengers?|people|persons?)\b|[.!?]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Email = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Phone = new(
        @"(?<!\w)\+?\d[\d\s().-]{7,}\d(?!\w)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Coordinates = new(
        @"(?<!\d)-?\d{1,3}\.\d{3,}\s*[,/]\s*-?\d{1,3}\.\d{3,}(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PaymentCard = new(
        @"(?<!\d)(?:\d[ -]?){13,19}(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Aadhaar = new(
        @"(?<!\d)\d{4}\s?\d{4}\s?\d{4}(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Pan = new(
        @"\b[A-Z]{5}\d{4}[A-Z]\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StreetAddress = new(
        @"\b\d{1,6}\s+[\p{L}\w.'-]+(?:\s+[\p{L}\w.'-]+){0,4}\s+(?:street|st|road|rd|avenue|ave|lane|ln|drive|dr|apartment|apt|flat)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Url = new(
        @"https?://\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex Jwt = new(
        @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string RedactTripIntent(string input)
    {
        var redacted = FromToRoute.Replace(input, " [route redacted] ");
        redacted = PickupDropoffRoute.Replace(redacted, " [route redacted] ");
        return RedactSensitiveValues(redacted);
    }

    public static string RedactQuestion(string input)
    {
        var redacted = FromToRoute.Replace(input, " [route redacted] ");
        redacted = PickupDropoffRoute.Replace(redacted, " [route redacted] ");
        return RedactSensitiveValues(redacted);
    }

    private static string RedactSensitiveValues(string input)
    {
        var redacted = Email.Replace(input, "[email redacted]");
        redacted = Phone.Replace(redacted, "[phone redacted]");
        redacted = Coordinates.Replace(redacted, "[coordinates redacted]");
        redacted = PaymentCard.Replace(redacted, "[payment data redacted]");
        redacted = Aadhaar.Replace(redacted, "[identity data redacted]");
        redacted = Pan.Replace(redacted, "[identity data redacted]");
        redacted = StreetAddress.Replace(redacted, "[address redacted]");
        redacted = Url.Replace(redacted, "[url redacted]");
        redacted = Jwt.Replace(redacted, "[token redacted]");
        return Regex.Replace(redacted, @"\s+", " ").Trim();
    }
}
