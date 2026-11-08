const { findActiveRide } = require("./rideRecovery.js");

function isDriverAvailabilityConflict(error) {
  return Boolean(error && typeof error === "object" && Number(error.status) === 409);
}

async function reconcileDriverAvailability({
  fetchAvailability,
  fetchRideHistory,
  isActive,
  setAvailabilityStatus,
  setDriverRideHistory,
  setActiveRide
}) {
  if (typeof fetchAvailability !== "function" ||
      typeof fetchRideHistory !== "function" ||
      typeof isActive !== "function" ||
      typeof setAvailabilityStatus !== "function" ||
      typeof setDriverRideHistory !== "function" ||
      typeof setActiveRide !== "function") {
    throw new TypeError("Driver availability reconciliation requires fetch, activity and state callbacks.");
  }

  const [availability, history] = await Promise.all([
    fetchAvailability(),
    fetchRideHistory()
  ]);

  if (!isActive()) {
    return { applied: false, isOnline: null, activeRide: null, shouldRetryLocation: false };
  }
  if (!availability || !Array.isArray(history)) {
    throw new TypeError("Driver availability reconciliation received an invalid server response.");
  }

  const isOnline = availability.availabilityStatus === "Online";
  const activeRide = findActiveRide(history, "driver");
  setAvailabilityStatus(isOnline ? "Online" : "Offline");
  setDriverRideHistory(history);
  setActiveRide(activeRide);

  return {
    applied: true,
    isOnline,
    activeRide,
    shouldRetryLocation: isOnline && activeRide === null
  };
}

module.exports = { isDriverAvailabilityConflict, reconcileDriverAvailability };
