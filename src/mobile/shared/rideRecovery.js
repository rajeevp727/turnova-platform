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

function applyRideStatusUpdate(ride, update) {
  if (!ride || !update || typeof update.rideId !== "string" || !update.rideId ||
      typeof update.status !== "string" || !update.status || ride.rideId !== update.rideId) {
    return ride;
  }
  const updated = { ...ride, status: update.status };
  if ("locationSharingActive" in ride) {
    updated.locationSharingActive = ["Accepted", "Arrived", "InProgress"].includes(update.status);
  }
  return updated;
}

function findActiveRide(history, role) {
  const allowedStatuses = ACTIVE_RIDE_STATUSES[role];
  if (!allowedStatuses) throw new TypeError("Unsupported ride recovery role.");
  return history.find(ride => ride && allowedStatuses.has(ride.status)) ?? null;
}

module.exports = { applyRideStatusUpdate, findActiveRide };
