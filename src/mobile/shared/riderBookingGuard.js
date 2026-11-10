const ACTIVE_RIDE_STATUSES = new Set([
  "Requested",
  "Matching",
  "Offered",
  "Negotiating",
  "AgreementConfirmed",
  "Accepted",
  "Arrived",
  "InProgress"
]);

function hasActiveRiderRide(ride) {
  return Boolean(ride && typeof ride.status === "string" && ACTIVE_RIDE_STATUSES.has(ride.status));
}

function canStartRiderBooking(rideStateLoaded, ride) {
  return rideStateLoaded === true && !hasActiveRiderRide(ride);
}

module.exports = { canStartRiderBooking, hasActiveRiderRide };
