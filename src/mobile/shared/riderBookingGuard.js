const TERMINAL_RIDE_STATUSES = new Set(["Completed", "Cancelled", "Expired"]);
const ACTIVE_RIDE_STATUSES = new Set(["Accepted", "Arrived", "InProgress"]);

const CONTROL_CHARACTERS = /[\u0000-\u001f\u007f-\u009f]/;

function isSafeTrimmedText(value, maxLength = 256) {
  return typeof value === "string" &&
    value.length > 0 &&
    value.length <= maxLength &&
    value === value.trim() &&
    !CONTROL_CHARACTERS.test(value);
}

function normalizeOptionalTimestamp(value) {
  if (value === null || value === undefined) return null;
  if (!isSafeTrimmedText(value, 64) || !Number.isFinite(Date.parse(value))) return null;
  return value;
}

function normalizeCoordinate(value, minimum, maximum) {
  return Number.isFinite(value) && value >= minimum && value <= maximum ? value : null;
}

/**
 * Validate the selected booking against the authenticated Rider and exact selected trip,
 * then project only fields needed by the Rider UI. Unknown lifecycle strings are retained
 * for neutral presentation; the booking guard still treats them as unresolved.
 *
 * @param {unknown} response
 * @param {{ riderId: string, driverId: string, unionId: string, pickup: string, dropoff: string, stops: string[], passengers: number, pickupLatitude: number, pickupLongitude: number }} expected
 * @returns {null | { rideId: string, pickup: string, dropoff: string, passengers: number, serviceClass: "cab", status: string, locationSharingActive: boolean, estimatedFareInr: null, stops: string[], unionId: string, unionName: string, driverDisplayName: string, driverVehicleMake: string, driverVehicleModel: string, driverDistanceToPickupKm: number, negotiationExpiresAtUtc: string | null, pickupLatitude: number, pickupLongitude: number, driverLatitude?: number, driverLongitude?: number, locationCapturedAtUtc: string | null, driverLocationCapturedAtUtc: string | null, locationUpdatedAt?: string, requestedAt: string, acceptedAt: string | null }}
 */
function normalizeRiderSelectedBookingResponse(response, expected) {
  const isRecord = value => value !== null && typeof value === "object" && !Array.isArray(value);
  if (!isRecord(response) || !isRecord(expected)) return null;

  const expectedStops = expected.stops;
  if (!isSafeTrimmedText(expected.riderId) ||
      !isSafeTrimmedText(expected.driverId, 128) ||
      !isSafeTrimmedText(expected.unionId, 128) ||
      !isSafeTrimmedText(expected.pickup, 250) ||
      !isSafeTrimmedText(expected.dropoff, 250) ||
      !Array.isArray(expectedStops) ||
      expectedStops.length > 10 ||
      expectedStops.some(stop => !isSafeTrimmedText(stop, 250)) ||
      !Number.isInteger(expected.passengers) ||
      expected.passengers < 1 || expected.passengers > 6) return null;

  // The server must echo the exact foreground GPS fix used in the selected-booking POST.
  // A merely valid but different coordinate pair is not the Rider's submitted pickup.
  const expectedPickupLatitude = normalizeCoordinate(expected.pickupLatitude, -90, 90);
  const expectedPickupLongitude = normalizeCoordinate(expected.pickupLongitude, -180, 180);
  if (expectedPickupLatitude === null || expectedPickupLongitude === null) return null;

  if (!isSafeTrimmedText(response.rideId, 128) ||
      !isSafeTrimmedText(response.riderId) || response.riderId !== expected.riderId ||
      !isSafeTrimmedText(response.driverId, 128) || response.driverId !== expected.driverId ||
      !isSafeTrimmedText(response.unionId, 128) || response.unionId !== expected.unionId ||
      !isSafeTrimmedText(response.pickup, 250) || response.pickup !== expected.pickup ||
      !isSafeTrimmedText(response.dropoff, 250) || response.dropoff !== expected.dropoff ||
      response.passengers !== expected.passengers ||
      response.serviceClass !== "cab" ||
      !isSafeTrimmedText(response.status, 64) ||
      typeof response.locationSharingActive !== "boolean" ||
      (response.locationSharingActive === true && !ACTIVE_RIDE_STATUSES.has(response.status)) ||
      (response.estimatedFareInr !== undefined && response.estimatedFareInr !== null) ||
      !Array.isArray(response.stops) ||
      response.stops.length !== expectedStops.length ||
      response.stops.some((stop, index) =>
        !isSafeTrimmedText(stop, 250) || stop !== expectedStops[index]) ||
      !isSafeTrimmedText(response.unionName) ||
      !isSafeTrimmedText(response.driverDisplayName) ||
      !isSafeTrimmedText(response.driverVehicleMake, 60) ||
      !isSafeTrimmedText(response.driverVehicleModel, 60) ||
      !Number.isFinite(response.driverDistanceToPickupKm) ||
      response.driverDistanceToPickupKm < 0) return null;

  const requestedAt = normalizeOptionalTimestamp(response.requestedAt);
  if (!requestedAt) return null;

  const pickupLatitude = normalizeCoordinate(response.pickupLatitude, -90, 90);
  const pickupLongitude = normalizeCoordinate(response.pickupLongitude, -180, 180);
  if (pickupLatitude === null || pickupLongitude === null ||
      pickupLatitude !== expectedPickupLatitude ||
      pickupLongitude !== expectedPickupLongitude) return null;

  const locationSharingActive = response.locationSharingActive === true &&
    ACTIVE_RIDE_STATUSES.has(response.status);
  const driverLatitude = normalizeCoordinate(response.driverLatitude, -90, 90);
  const driverLongitude = normalizeCoordinate(response.driverLongitude, -180, 180);
  const hasDriverLocation = locationSharingActive &&
    driverLatitude !== null && driverLongitude !== null;

  const locationUpdatedAt = normalizeOptionalTimestamp(response.locationUpdatedAt);
  return {
    rideId: response.rideId,
    pickup: response.pickup,
    dropoff: response.dropoff,
    passengers: response.passengers,
    serviceClass: "cab",
    status: response.status,
    locationSharingActive,
    estimatedFareInr: null,
    stops: [...response.stops],
    unionId: response.unionId,
    unionName: response.unionName,
    driverDisplayName: response.driverDisplayName,
    driverVehicleMake: response.driverVehicleMake,
    driverVehicleModel: response.driverVehicleModel,
    driverDistanceToPickupKm: response.driverDistanceToPickupKm,
    negotiationExpiresAtUtc: normalizeOptionalTimestamp(response.negotiationExpiresAtUtc),
    pickupLatitude,
    pickupLongitude,
    ...(hasDriverLocation ? { driverLatitude, driverLongitude } : {}),
    locationCapturedAtUtc: normalizeOptionalTimestamp(response.locationCapturedAtUtc),
    driverLocationCapturedAtUtc: normalizeOptionalTimestamp(response.driverLocationCapturedAtUtc),
    ...(locationUpdatedAt ? { locationUpdatedAt } : {}),
    requestedAt,
    acceptedAt: normalizeOptionalTimestamp(response.acceptedAt)
  };
}


function hasActiveRiderRide(ride) {
  if (!ride || typeof ride !== "object") return false;
  // Unknown or malformed states are not proof that a ride is terminal. Fail closed
  // until the API returns one of the explicitly supported terminal statuses.
  return typeof ride.status !== "string" || !TERMINAL_RIDE_STATUSES.has(ride.status);
}

function canStartRiderBooking(rideStateLoaded, ride) {
  return rideStateLoaded === true && !hasActiveRiderRide(ride);
}

module.exports = { canStartRiderBooking, hasActiveRiderRide, normalizeRiderSelectedBookingResponse };
