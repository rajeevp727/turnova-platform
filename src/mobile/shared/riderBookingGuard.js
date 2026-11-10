const ACTIVE_RIDE_STATUSES = new Set([
  "Requested",
  "Negotiating",
  "AgreementConfirmed",
  "Accepted",
  "Arrived",
  "InProgress"
]);

function hasActiveRiderRide(ride) {
  return Boolean(ride && typeof ride.status === "string" && ACTIVE_RIDE_STATUSES.has(ride.status));
}

module.exports = { hasActiveRiderRide };
