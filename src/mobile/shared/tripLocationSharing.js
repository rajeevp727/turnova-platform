const ACTIVE_TRIP_STATUSES = new Set(["Accepted", "Arrived", "InProgress"]);

function canShareTripLocation(state) {
  return Boolean(
    state &&
    state.mounted === true &&
    state.appState === "active" &&
    state.locationSharingActive === true &&
    ACTIVE_TRIP_STATUSES.has(state.status)
  );
}

function findAuthoritativeActiveTrip(history, rideId) {
  if (!Array.isArray(history) || typeof rideId !== "string" || !rideId) return null;
  return history.find(ride =>
    ride &&
    ride.rideId === rideId &&
    ACTIVE_TRIP_STATUSES.has(ride.status) &&
    ride.locationSharingActive === true
  ) ?? null;
}

module.exports = { canShareTripLocation, findAuthoritativeActiveTrip };
