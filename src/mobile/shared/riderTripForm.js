"use strict";

const TRIP_FIELDS = Object.freeze({
  tripIntent: {
    accessibilityLabel: "Describe your trip",
    accessibilityHint: "Optional. Describe your pickup, destination and passenger count. This creates an editable draft only; it does not book or charge."
  },
  pickup: {
    accessibilityLabel: "Pickup landmark or note",
    accessibilityHint: "Enter a pickup landmark or instruction. Device GPS is used for pickup-zone matching and driver-to-pickup proximity."
  },
  dropoff: {
    accessibilityLabel: "Destination landmark or note",
    accessibilityHint: "Enter the destination landmark or instruction. This text is not used to estimate the journey distance."
  },
  stops: {
    accessibilityLabel: "Optional intermediate stops",
    accessibilityHint: "Optional. Enter one stop per line in the order you want to visit them."
  },
  passengers: {
    accessibilityLabel: "Passenger count",
    accessibilityHint: "Enter a whole number from 1 to 6."
  }
});

const TRIP_ACTIONS = Object.freeze({
  draft: {
    label: "Draft trip details",
    busyLabel: "Preparing trip draft",
    hint: "Creates an editable trip draft only. Review and edit the fields before searching; this does not book or charge."
  },
  discover: {
    label: "Find nearby Cab drivers",
    busyLabel: "Searching nearby Cab drivers",
    hint: "Finds eligible available Cab drivers in approved Cab-serving unions near your pickup. You choose a driver; no automatic assignment or booking occurs."
  },
  requestDriver: {
    label: "Request this driver",
    busyLabel: "Requesting selected driver",
    hint: "Sends the selected Cab driver a request for direct fare negotiation. TurnOva does not quote or collect payment."
  }
});

function getRiderTripFieldProps(field, disabled) {
  const definition = TRIP_FIELDS[field];
  if (!definition) throw new RangeError("Unknown Rider trip field.");
  return {
    accessibilityLabel: definition.accessibilityLabel,
    accessibilityHint: definition.accessibilityHint,
    accessibilityState: { disabled: Boolean(disabled) }
  };
}

function getRiderTripActionProps(action, disabled, busy) {
  const definition = TRIP_ACTIONS[action];
  if (!definition) throw new RangeError("Unknown Rider trip action.");
  return {
    accessibilityRole: "button",
    accessibilityLabel: busy ? definition.busyLabel : definition.label,
    accessibilityHint: definition.hint,
    accessibilityState: { disabled: Boolean(disabled), busy: Boolean(busy) }
  };
}

function canDiscoverRiderTrip(pickup, dropoff, passengerCount) {
  return typeof pickup === "string" && pickup.trim().length > 0 &&
    typeof dropoff === "string" && dropoff.trim().length > 0 &&
    Number.isInteger(passengerCount) && passengerCount >= 1 && passengerCount <= 6;
}

function shouldApplyRiderDiscoveryResponse(requestSequence, currentSequence) {
  return Number.isSafeInteger(requestSequence) && requestSequence > 0 &&
    Number.isSafeInteger(currentSequence) && requestSequence === currentSequence;
}

function normalizeRiderDiscoveryResponse(response) {
  const isRecord = value => value !== null && typeof value === "object" && !Array.isArray(value);
  const isTrimmedNonEmptyString = value =>
    typeof value === "string" && value.length > 0 && value === value.trim();

  if (!isRecord(response) ||
      !isTrimmedNonEmptyString(response.pickupZoneId) ||
      !(response.journeyDistanceKm === null ||
        (Number.isFinite(response.journeyDistanceKm) && response.journeyDistanceKm >= 0)) ||
      !Array.isArray(response.unions)) return null;

  const seenUnionIds = new Set();
  const unions = [];
  for (const union of response.unions) {
    if (!isRecord(union) ||
        !isTrimmedNonEmptyString(union.unionId) ||
        !isTrimmedNonEmptyString(union.unionName) ||
        !Array.isArray(union.drivers) ||
        seenUnionIds.has(union.unionId)) return null;
    seenUnionIds.add(union.unionId);

    const seenDriverIds = new Set();
    const drivers = [];
    for (const driver of union.drivers) {
      if (!isRecord(driver) ||
          !isTrimmedNonEmptyString(driver.driverId) ||
          !isTrimmedNonEmptyString(driver.fullName) ||
          typeof driver.vehicleMake !== "string" || driver.vehicleMake.trim().length === 0 || driver.vehicleMake.trim().length > 60 ||
          typeof driver.vehicleModel !== "string" || driver.vehicleModel.trim().length === 0 || driver.vehicleModel.trim().length > 60 ||
          driver.serviceClass !== "cab" ||
          !Number.isInteger(driver.passengerCapacity) ||
          driver.passengerCapacity < 1 || driver.passengerCapacity > 6 ||
          !Number.isFinite(driver.distanceToPickupKm) || driver.distanceToPickupKm < 0 ||
          (driver.profilePhotoBase64 !== undefined && driver.profilePhotoBase64 !== null &&
            (typeof driver.profilePhotoBase64 !== "string" || driver.profilePhotoBase64.length === 0 || driver.profilePhotoBase64.length > 410000)) ||
          (driver.profilePhotoContentType !== undefined && driver.profilePhotoContentType !== null &&
            typeof driver.profilePhotoContentType !== "string") ||
          seenDriverIds.has(driver.driverId)) return null;

      const photo = driver.profilePhotoBase64 ?? null;
      const contentType = driver.profilePhotoContentType ?? null;
      if (photo !== null && (!photo.startsWith("/9j/") || photo.length % 4 !== 0)) return null;
      if ((photo === null && contentType !== null) ||
          (photo !== null && contentType !== null && contentType !== "image/jpeg") ||
          (photo !== null && driver.profilePhotoApprovalStatus !== undefined && driver.profilePhotoApprovalStatus !== "Approved") ||
          (photo !== null && driver.profilePhotoApprovedByUnionId !== undefined && driver.profilePhotoApprovedByUnionId !== union.unionId)) return null;

      seenDriverIds.add(driver.driverId);
      drivers.push({
        driverId: driver.driverId,
        fullName: driver.fullName,
        vehicleMake: driver.vehicleMake.trim(),
        vehicleModel: driver.vehicleModel.trim(),
        serviceClass: "cab",
        passengerCapacity: driver.passengerCapacity,
        distanceToPickupKm: driver.distanceToPickupKm,
        profilePhotoBase64: photo,
        profilePhotoContentType: photo === null ? null : (contentType ?? "image/jpeg")
      });
    }

    unions.push({ unionId: union.unionId, unionName: union.unionName, drivers });
  }

  // Project only the public Rider discovery contract; ignore unexpected private fields.
  return {
    pickupZoneId: response.pickupZoneId,
    journeyDistanceKm: response.journeyDistanceKm,
    unions
  };
}

function getVisibleUnionDrivers(drivers) {
  return Array.isArray(drivers) ? drivers.slice(0, 3) : [];
}

function getRiderDriverOptionProps(driver, selected, disabled, unionName = "") {
  const name = typeof driver?.fullName === "string" && driver.fullName.trim()
    ? driver.fullName.trim()
    : "Cab driver";
  const vehicle = [driver?.vehicleMake, driver?.vehicleModel]
    .filter(value => typeof value === "string" && value.trim())
    .join(" ") || "verified Cab vehicle";
  const capacity = Number.isInteger(driver?.passengerCapacity) && driver.passengerCapacity > 0
    ? driver.passengerCapacity
    : null;
  const distance = Number.isFinite(driver?.distanceToPickupKm)
    ? driver.distanceToPickupKm.toFixed(1) + " kilometres from pickup"
    : "pickup distance unavailable";
  const union = typeof unionName === "string" && unionName.trim()
    ? ", " + unionName.trim() + " union"
    : "";

  return {
    accessibilityRole: "radio",
    accessibilityLabel: name + ", " + vehicle + ", Cab" + union +
      (capacity ? ", capacity " + capacity + " passengers" : "") + ", " + distance,
    accessibilityHint: "Select this approved Cab driver. The distance shown is to pickup, not journey distance.",
    accessibilityState: { checked: Boolean(selected), disabled: Boolean(disabled) }
  };
}

module.exports = {
  canDiscoverRiderTrip,
  shouldApplyRiderDiscoveryResponse,
  normalizeRiderDiscoveryResponse,
  getVisibleUnionDrivers,
  getRiderDriverOptionProps,
  getRiderTripActionProps,
  getRiderTripFieldProps
};
