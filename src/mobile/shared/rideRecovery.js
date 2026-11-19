const { isStrictIsoTimestampWithOffset } = require("./riderTimestamp");

const ACTIVE_RIDE_STATUSES = {
  rider: new Set([
    "Requested",
    "Matching",
    "Offered",
    "Negotiating",
    "AgreementConfirmed",
    "Accepted",
    "Arrived",
    "InProgress"
  ]),
  driver: new Set([
    "Negotiating",
    "AgreementConfirmed",
    "Accepted",
    "Arrived",
    "InProgress"
  ])
};

const KNOWN_RIDE_STATUSES = new Set([
  "Requested", "Matching", "Offered", "Negotiating", "AgreementConfirmed",
  "Accepted", "Arrived", "InProgress", "Completed", "Cancelled", "Expired"
]);

function isSafeHistoryText(value, maximumLength = 1000) {
  return typeof value === "string" &&
    value.trim().length > 0 &&
    value.length <= maximumLength &&
    !/[\\u0000-\\u001f\\u007f-\\u009f]/.test(value);
}

function normalizeRiderRideHistory(history) {
  if (!Array.isArray(history)) return null;

  const seenRideIds = new Set();
  const normalized = [];
  for (const item of history) {
    if (!item || typeof item !== "object" || Array.isArray(item) ||
        typeof item.rideId !== "string" ||
        item.rideId.length === 0 ||
        item.rideId.length > 128 ||
        item.rideId !== item.rideId.trim() ||
        /[\\u0000-\\u001f\\u007f-\\u009f]/.test(item.rideId) ||
        seenRideIds.has(item.rideId)) return null;
    seenRideIds.add(item.rideId);

    const serviceClass = typeof item.serviceClass === "string" &&
      /^[A-Za-z][A-Za-z0-9_-]{0,31}$/.test(item.serviceClass)
      ? item.serviceClass
      : "unknown";
    const status = typeof item.status === "string" &&
      item.status.length <= 64 &&
      item.status === item.status.trim() &&
      KNOWN_RIDE_STATUSES.has(item.status) &&
      !/[\\u0000-\\u001f\\u007f-\\u009f]/.test(item.status)
      ? item.status
      : "Unknown";
    const requestedAt = typeof item.requestedAt === "string" &&
      isStrictIsoTimestampWithOffset(item.requestedAt)
      ? item.requestedAt
      : "";
    const acceptedAt = typeof item.acceptedAt === "string" &&
      isStrictIsoTimestampWithOffset(item.acceptedAt)
      ? item.acceptedAt
      : null;
    const stops = Array.isArray(item.stops) && item.stops.length <= 10
      ? item.stops.filter(stop => isSafeHistoryText(stop, 250)).map(stop => stop.trim())
      : [];

    normalized.push({
      rideId: item.rideId,
      pickup: isSafeHistoryText(item.pickup) ? item.pickup.trim() : "Pickup unavailable",
      dropoff: isSafeHistoryText(item.dropoff) ? item.dropoff.trim() : "Drop-off unavailable",
      passengers: Number.isInteger(item.passengers) && item.passengers >= 0 && item.passengers <= 8
        ? item.passengers
        : null,
      serviceClass,
      status,
      requestedAt,
      acceptedAt,
      stops
    });
  }

  return normalized;
}

function isRiderRideHistoryShapeValid(history) {
  return normalizeRiderRideHistory(history) !== null;
}

function isRiderRideDetailsResponseValid(response, expectedRideId) {
  if (typeof expectedRideId !== "string" || expectedRideId.length === 0 || expectedRideId !== expectedRideId.trim()) return false;
  if (!response || typeof response !== "object" || Array.isArray(response) ||
      typeof response.rideId !== "string" || response.rideId !== expectedRideId) return false;

  // The history projection selected an active Rider ride. A terminal, missing or
  // unknown detail status cannot safely re-enable booking while the two reads disagree.
  if (typeof response.status !== "string" ||
      !ACTIVE_RIDE_STATUSES.rider.has(response.status)) return false;

  const optionalTimestampFields = [
    "requestedAt",
    "acceptedAt",
    "negotiationExpiresAtUtc",
    "locationUpdatedAt",
    "locationCapturedAtUtc",
    "driverLocationCapturedAtUtc"
  ];
  return optionalTimestampFields.every(field => {
    const value = response[field];
    return value === undefined || value === null || isStrictIsoTimestampWithOffset(value);
  });
}

function shouldApplyRideHistoryResponse(requestSequence, currentSequence) {
  return Number.isSafeInteger(requestSequence) &&
    requestSequence > 0 &&
    Number.isSafeInteger(currentSequence) &&
    requestSequence === currentSequence;
}

function hasUnresolvedRiderRideHistory(history) {
  if (!Array.isArray(history)) return true;

  let activeRiderRideCount = 0;
  for (const ride of history) {
    // Unknown/malformed lifecycle values cannot be assumed terminal, even when a
    // different row has a known active status.
    if (!ride ||
        typeof ride !== "object" ||
        Array.isArray(ride) ||
        typeof ride.status !== "string" ||
        !KNOWN_RIDE_STATUSES.has(ride.status)) return true;

    // Multiple known active records are also ambiguous (for example, a legacy
    // Offered record alongside a selected negotiation). Never pick one arbitrarily.
    if (ACTIVE_RIDE_STATUSES.rider.has(ride.status)) {
      activeRiderRideCount += 1;
      if (activeRiderRideCount > 1) return true;
    }
  }

  return false;
}

function findActiveRide(history, role) {
  const allowedStatuses = ACTIVE_RIDE_STATUSES[role];
  if (!allowedStatuses) throw new TypeError("Unsupported ride recovery role.");

  const knownActiveRide = history.find(ride => ride && allowedStatuses.has(ride.status));
  if (knownActiveRide) return knownActiveRide;

  // Rider must not treat a future or malformed server lifecycle value as terminal:
  // doing so could re-enable a second booking while the existing ride is unresolved.
  // Known active states take precedence; Driver's legacy Offered exclusion is unchanged.
  if (role === "rider") {
    return history.find(ride => ride && typeof ride === "object" && !Array.isArray(ride) &&
      (typeof ride.status !== "string" || !KNOWN_RIDE_STATUSES.has(ride.status))) ?? null;
  }

  return null;
}

function applyRideStatusUpdate(ride, update) {
  if (!ride || !update ||
      typeof update.rideId !== "string" || update.rideId.trim().length === 0 ||
      typeof update.status !== "string" || update.status.trim().length === 0 ||
      ride.rideId !== update.rideId) {
    return ride;
  }

  const updated = { ...ride, status: update.status };
  if ("locationSharingActive" in ride) {
    updated.locationSharingActive = ["Accepted", "Arrived", "InProgress"].includes(update.status);
  }
  return updated;
}

function shouldApplyRideStatusUpdate(currentRideId, updateRideId, isActive) {
  return isActive === true &&
    typeof currentRideId === "string" && currentRideId.trim().length > 0 &&
    typeof updateRideId === "string" && updateRideId === currentRideId;
}

module.exports = { applyRideStatusUpdate, findActiveRide, hasUnresolvedRiderRideHistory, isRiderRideDetailsResponseValid, isRiderRideHistoryShapeValid, normalizeRiderRideHistory, shouldApplyRideHistoryResponse, shouldApplyRideStatusUpdate };
