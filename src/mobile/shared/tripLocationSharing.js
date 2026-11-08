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

  const hasCaptureValue = update.locationCapturedAtUtc !== undefined &&
    update.locationCapturedAtUtc !== null;
  const currentCapture = parseUtcTimestamp(
    current.locationCapturedAtUtc ?? current.driverLocationCapturedAtUtc
  );
  if (hasCaptureValue) {
    const incomingCapture = parseUtcTimestamp(update.locationCapturedAtUtc);
    if (incomingCapture === null) return false;
    // Never compare a GPS capture timestamp with a legacy server-receipt timestamp.
    // The first capture watermark is accepted when upgrading a legacy ride snapshot.
    return currentCapture === null || incomingCapture > currentCapture;
  }

  // Once a capture watermark exists, do not let an event from an older API instance
  // (which cannot emit capture time) downgrade the current snapshot to receipt ordering.
  if (currentCapture !== null) return false;
  const incomingReceipt = parseUtcTimestamp(update.locationUpdatedAt);
  const currentReceipt = parseUtcTimestamp(current.locationUpdatedAt);
  return incomingReceipt !== null &&
    (currentReceipt === null || incomingReceipt > currentReceipt);
}

function mergeTripLocationSnapshot(current, snapshot) {
  if (!snapshot) return current;
  if (!current || current.rideId !== snapshot.rideId) return snapshot;

  const captureTimestamp = snapshot.driverLocationCapturedAtUtc ??
    snapshot.locationCapturedAtUtc;
  const incomingLocation = {
    rideId: snapshot.rideId,
    latitude: snapshot.driverLatitude,
    longitude: snapshot.driverLongitude,
    locationCapturedAtUtc: captureTimestamp,
    locationUpdatedAt: snapshot.locationUpdatedAt
  };

  if (shouldApplyTripLocationUpdate(current, incomingLocation)) {
    return {
      ...current,
      ...snapshot,
      locationCapturedAtUtc: captureTimestamp ?? current.locationCapturedAtUtc
    };
  }

  // A reconnect/history response can have been read before a newer SignalR event.
  // Merge non-location state, but never replace a newer client-side location snapshot.
  return {
    ...current,
    ...snapshot,
    driverLatitude: current.driverLatitude ?? snapshot.driverLatitude,
    driverLongitude: current.driverLongitude ?? snapshot.driverLongitude,
    locationCapturedAtUtc: current.locationCapturedAtUtc ??
      current.driverLocationCapturedAtUtc ??
      snapshot.driverLocationCapturedAtUtc ??
      snapshot.locationCapturedAtUtc,
    driverLocationCapturedAtUtc: current.driverLocationCapturedAtUtc ??
      snapshot.driverLocationCapturedAtUtc,
    locationUpdatedAt: current.locationUpdatedAt ?? snapshot.locationUpdatedAt
  };
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

function shouldFitRideMap({ tab, rideId, readyRideId, followDriverLocation }) {
  return tab === "Activity" &&
    typeof rideId === "string" && rideId.length > 0 &&
    readyRideId === rideId &&
    followDriverLocation === true;
}

module.exports = {
  canShareTripLocation,
  findAuthoritativeActiveTrip,
  shouldApplyTripLocationUpdate,
  mergeTripLocationSnapshot,
  getRideMapFitCoordinates,
  shouldFitRideMap
};
