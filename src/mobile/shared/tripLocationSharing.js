const ACTIVE_TRIP_STATUSES = new Set(["Accepted", "Arrived", "InProgress"]);
const DRIVER_RIDE_LIFECYCLE_STATUSES = new Set(["Negotiating", "AgreementConfirmed", ...ACTIVE_TRIP_STATUSES]);

function shouldMaintainDriverRideLifecycle(state) {
  return Boolean(
    state &&
    typeof state.userId === "string" && state.userId.trim().length > 0 &&
    typeof state.rideId === "string" && state.rideId.trim().length > 0 &&
    state.hasAccessToken === true &&
    state.sessionCurrent === true &&
    DRIVER_RIDE_LIFECYCLE_STATUSES.has(state.status)
  );
}

function shouldWatchDriverTripLocation(state) {
  return Boolean(
    shouldMaintainDriverRideLifecycle(state) &&
    ACTIVE_TRIP_STATUSES.has(state.status) &&
    state.locationSharingActive === true
  );
}

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

const TERMINAL_TRIP_STATUSES = new Set(["Completed", "Cancelled", "Expired"]);

function isRiderLocationSharingActive(ride) {
  return Boolean(
    ride &&
    ACTIVE_TRIP_STATUSES.has(ride.status) &&
    ride.locationSharingActive === true
  );
}

function canShowRiderDriverLocation(ride) {
  return Boolean(
    isRiderLocationSharingActive(ride) &&
    areValidCoordinates(ride.driverLatitude, ride.driverLongitude)
  );
}

function clearDriverLocation(ride) {
  return {
    ...ride,
    locationSharingActive: false,
    driverLatitude: undefined,
    driverLongitude: undefined,
    driverLocationCapturedAtUtc: null,
    locationCapturedAtUtc: null,
    locationUpdatedAt: undefined
  };
}

function applyRiderRideStatusUpdate(current, update) {
  if (!current || !update ||
      typeof current.rideId !== "string" ||
      current.rideId !== update.rideId ||
      typeof update.status !== "string") return current;

  // Terminal states cannot be regressed by a late/out-of-order SignalR event.
  if (TERMINAL_TRIP_STATUSES.has(current.status) && current.status !== update.status) {
    return clearDriverLocation(current);
  }

  if (isRiderLocationSharingActive(current) && ACTIVE_TRIP_STATUSES.has(update.status)) {
    return { ...current, status: update.status, locationSharingActive: true };
  }

  return clearDriverLocation({ ...current, status: update.status });
}

function shouldApplyTripLocationUpdate(current, update) {
  if (!isRiderLocationSharingActive(current) ||
      !update ||
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
  if (!current || current.rideId !== snapshot.rideId) {
    return isRiderLocationSharingActive(snapshot) ? snapshot : clearDriverLocation(snapshot);
  }

  // Never resurrect a terminal ride from a stale history snapshot.
  if (TERMINAL_TRIP_STATUSES.has(current.status) &&
      !TERMINAL_TRIP_STATUSES.has(snapshot.status)) {
    return clearDriverLocation(current);
  }

  // Server-authoritative pause/terminal state clears any previously cached Driver GPS.
  if (!isRiderLocationSharingActive(snapshot)) {
    return clearDriverLocation({ ...current, ...snapshot });
  }

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
  if (canShowRiderDriverLocation(ride)) {
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
  shouldMaintainDriverRideLifecycle,
  shouldWatchDriverTripLocation,
  canShareTripLocation,
  findAuthoritativeActiveTrip,
  shouldApplyTripLocationUpdate,
  mergeTripLocationSnapshot,
  applyRiderRideStatusUpdate,
  canShowRiderDriverLocation,
  getRideMapFitCoordinates,
  shouldFitRideMap
};
