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

module.exports = { applyRideStatusUpdate, findActiveRide, shouldApplyRideStatusUpdate };
