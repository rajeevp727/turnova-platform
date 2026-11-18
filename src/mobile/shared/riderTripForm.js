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
  getRiderDriverOptionProps,
  getRiderTripActionProps,
  getRiderTripFieldProps
};
