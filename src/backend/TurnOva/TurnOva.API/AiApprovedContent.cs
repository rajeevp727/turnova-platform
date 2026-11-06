namespace TurnOva.API;

public sealed record AiFaqEntry(string Id, string[] Keywords, string Answer);

/// <summary>
/// Only these approved answers may be returned by the product-help endpoint.
/// A provider may classify a topic ID, but it cannot author product policy or free-form answers.
/// </summary>
public static class AiApprovedContent
{
    private static readonly AiFaqEntry[] Entries =
    [
        new("cab-only",
            ["cab", "vehicle category", "bike", "scooty", "scooter", "auto"],
            "TurnOva's current MVP supports Cab rides only. Bike, Scooty and Auto are not available."),
        new("driver-discovery",
            ["nearby driver", "find driver", "union", "available driver", "driver discovery"],
            "A Rider enters pickup, destination and optional stops. TurnOva shows eligible, approved and available Cab drivers grouped under active approved unions. The Rider selects a driver. Driver-to-pickup proximity is separate from the From-to-To journey distance."),
        new("fare-negotiation",
            ["fare", "negotiate", "counter offer", "agreement", "confirm fare"],
            "The Rider and selected Driver negotiate the fare directly. TurnOva does not set or recommend a fare. Both parties must confirm the current agreement before the Driver starts the ride. A renegotiation creates a new agreement version and requires both confirmations."),
        new("direct-payment",
            ["payment", "pay", "paid", "cash", "amount received", "collect money"],
            "The Rider pays the Driver directly; TurnOva does not collect payment or hold escrow. The Driver reports the amount received. That report is driver-reported and unverified, not customer- or bank-verified."),
        new("ride-lifecycle",
            ["ride status", "ride state", "start ride", "complete ride", "trip status"],
            "A selected-driver ride proceeds through driver selection, negotiation, mutually confirmed agreement, in-progress ride, completion and Driver payment reporting. The Driver cannot start without a current agreement confirmed by both parties."),
        new("rider-selection",
            ["fifo", "queue", "automatic assignment", "select driver", "choose driver"],
            "The Rider booking flow uses customer-selected-driver discovery. It does not automatically assign a Driver using FIFO. The internal Union queue fairness policy is not defined in approved Rider help content."),
        new("selfie-approval",
            ["selfie", "profile photo", "driver photo", "vehicle verification"],
            "A Driver profile photo is shown publicly only after the relevant Union approves the selfie. Vehicle details must be verified and approved before appearing in discovery."),
        new("payment-reconciliation",
            ["delta", "shortfall", "outstanding", "excess", "reconciliation"],
            "The reconciliation delta is agreed fare minus the Driver-reported amount received. A positive value means reported shortfall, zero means exact match and a negative value means reported excess. The amount remains driver-reported and unverified.")
    ];

    public static IReadOnlyCollection<string> TopicIds => Entries.Select(entry => entry.Id).ToArray();

    public static AiFaqEntry? FindById(string? id) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));

    public static AiFaqEntry? Match(string question) =>
        Entries.FirstOrDefault(entry => entry.Keywords.Any(keyword =>
            question.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
}
