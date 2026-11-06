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

module.exports = { canShareTripLocation };
