namespace TurnOva.API;

public static class UnionReconciliationQuery
{
    // Keep this projection deliberately flat and limited to fields returned to
    // authorised Union managers. Never select the full Rides aggregate here.
    public const string Sql =
        "SELECT c.serviceClass, c.unionId, " +
        "c.paymentReconciliation.rideId AS rideId, " +
        "c.paymentReconciliation.agreementVersion AS agreementVersion, " +
        "c.paymentReconciliation.unionId AS reportUnionId, " +
        "c.paymentReconciliation.agreedAmount AS agreedAmount, " +
        "c.paymentReconciliation.amountReceived AS amountReceived, " +
        "c.paymentReconciliation.delta AS delta, " +
        "c.paymentReconciliation.currency AS currency, " +
        "c.paymentReconciliation.reportedAtUtc AS reportedAtUtc, " +
        "c.paymentReconciliation.reportedByDriverId AS reportedByDriverId, " +
        "c.paymentReconciliation.status AS status " +
        "FROM c WHERE c.unionId = @unionId " +
        "AND IS_DEFINED(c.paymentReconciliation) " +
        "AND c.paymentReconciliation.unionId = @unionId";
}

public sealed record UnionReconciliationQueryProjection(
    string? ServiceClass,
    string? UnionId,
    string RideId,
    int AgreementVersion,
    string ReportUnionId,
    decimal AgreedAmount,
    decimal AmountReceived,
    decimal Delta,
    string Currency,
    DateTimeOffset ReportedAtUtc,
    string ReportedByDriverId,
    string Status);
