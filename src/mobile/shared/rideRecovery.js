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

function isActiveRide(ride, role) {
  const allowedStatuses = ACTIVE_RIDE_STATUSES[role];
  if (!allowedStatuses) throw new TypeError("Unsupported ride recovery role.");
  return Boolean(ride && allowedStatuses.has(ride.status));
}

function canShareActiveRideLocation(ride) {
  return Boolean(
    ride &&
    ride.locationSharingActive === true &&
    ["Accepted", "Arrived", "InProgress"].includes(ride.status)
  );
}

function findActiveRide(history, role) {
  const allowedStatuses = ACTIVE_RIDE_STATUSES[role];
  if (!allowedStatuses) throw new TypeError("Unsupported ride recovery role.");
  return history.find(ride => ride && allowedStatuses.has(ride.status)) ?? null;
}

module.exports = { findActiveRide, isActiveRide, canShareActiveRideLocation };
