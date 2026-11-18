const TERMINAL_RIDE_STATUSES = new Set(["Completed", "Cancelled", "Expired"]);

function hasActiveRiderRide(ride) {
  if (!ride || typeof ride !== "object") return false;
  // Unknown or malformed states are not proof that a ride is terminal. Fail closed
  // until the API returns one of the explicitly supported terminal statuses.
  return typeof ride.status !== "string" || !TERMINAL_RIDE_STATUSES.has(ride.status);
}

function canStartRiderBooking(rideStateLoaded, ride) {
  return rideStateLoaded === true && !hasActiveRiderRide(ride);
}

module.exports = { canStartRiderBooking, hasActiveRiderRide };
