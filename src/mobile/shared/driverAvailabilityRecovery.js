const { findActiveRide } = require("./rideRecovery.js");

function isDriverAvailabilityConflict(error) {
  return Boolean(error && typeof error === "object" && Number(error.status) === 409);
}

const DRIVER_ZONE_EXIT_MESSAGE = "You left your assigned service zone and were taken offline.";

function isDriverZoneExit(error) {
  return Boolean(error &&
    typeof error === "object" &&
    Number(error.status) === 409 &&
    typeof error.message === "string" &&
    error.message.trim() === DRIVER_ZONE_EXIT_MESSAGE);
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

const DRIVER_LOCATION_RETRY_DELAY_MS = 15000;

function isDriverTransientLocationError(error) {
  if (!error || typeof error !== "object") return false;
  if (error instanceof TypeError || error.name === "TypeError") return true;

  const status = Number(error.status);
  return status === 408 || status === 429 || (status >= 500 && status <= 599);
}

function createDriverLocationRetryScheduler({
  canRetry,
  retry,
  setTimeoutFn = setTimeout,
  clearTimeoutFn = clearTimeout
}) {
  if (typeof canRetry !== "function" ||
      typeof retry !== "function" ||
      typeof setTimeoutFn !== "function" ||
      typeof clearTimeoutFn !== "function") {
    throw new TypeError("Driver location retry scheduling requires activity, retry and timer callbacks.");
  }

  let timer;

  return {
    schedule() {
      if (timer !== undefined || !canRetry()) return false;
      timer = setTimeoutFn(() => {
        timer = undefined;
        if (canRetry()) void retry();
      }, DRIVER_LOCATION_RETRY_DELAY_MS);
      return true;
    },
    cancel() {
      if (timer === undefined) return false;
      clearTimeoutFn(timer);
      timer = undefined;
      return true;
    },
    isPending() {
      return timer !== undefined;
    }
  };
}

module.exports = { isDriverAvailabilityConflict, isDriverZoneExit, reconcileDriverAvailability , isDriverTransientLocationError, createDriverLocationRetryScheduler, DRIVER_LOCATION_RETRY_DELAY_MS};
