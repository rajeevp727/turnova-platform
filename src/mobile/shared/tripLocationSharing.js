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

function parseUtcTimestamp(value) {
  if (typeof value !== "string" || value.trim().length === 0) return null;
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function areValidCoordinates(latitude, longitude) {
  return Number.isFinite(latitude) && latitude >= -90 && latitude <= 90 &&
    Number.isFinite(longitude) && longitude >= -180 && longitude <= 180;
}

function shouldApplyTripLocationUpdate(current, update) {
  if (!current || !update ||
      typeof current.rideId !== "string" || !current.rideId ||
      update.rideId !== current.rideId ||
      !areValidCoordinates(update.latitude, update.longitude)) return false;

  const incomingCapture = parseUtcTimestamp(update.locationCapturedAtUtc);
  const currentCapture = parseUtcTimestamp(current.locationCapturedAtUtc);
  if (incomingCapture !== null) {
    // Never compare a GPS capture timestamp with a legacy server-receipt timestamp.
    // The first capture watermark is accepted when upgrading a legacy ride snapshot.
    return currentCapture === null || incomingCapture > currentCapture;
  }

  const incomingReceipt = parseUtcTimestamp(update.locationUpdatedAt);
  const currentReceipt = parseUtcTimestamp(current.locationUpdatedAt);
  return incomingReceipt !== null &&
    (currentReceipt === null || incomingReceipt > currentReceipt);
}

function getRideMapFitCoordinates(ride) {
  if (!ride) return [];
  const coordinates = [];
  if (areValidCoordinates(ride.driverLatitude, ride.driverLongitude)) {
    coordinates.push({ latitude: ride.driverLatitude, longitude: ride.driverLongitude });
  }
  if (areValidCoordinates(ride.pickupLatitude, ride.pickupLongitude)) {
    coordinates.push({ latitude: ride.pickupLatitude, longitude: ride.pickupLongitude });
  }
  return coordinates;
}

module.exports = {
  canShareTripLocation,
  findAuthoritativeActiveTrip,
  shouldApplyTripLocationUpdate,
  getRideMapFitCoordinates
};
