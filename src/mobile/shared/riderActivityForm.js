"use strict";

const ACTIVITY_FIELDS = Object.freeze({
  agreementAmount: {
    accessibilityLabel: "Proposed or counter fare amount",
    accessibilityHint: "Enter the amount you want to propose or counterpropose directly to the selected Driver. TurnOva does not estimate, set or collect fares."
  }
});

const ACTIVITY_ACTIONS = Object.freeze({
  propose: {
    label: "Propose or counter fare",
    busyLabel: "Submitting fare proposal",
    hint: "Send this amount to the selected Driver for direct negotiation. Both parties must confirm the current agreement version before the ride can start. TurnOva does not collect payment."
  },
  confirm: {
    label: "Confirm current agreement version",
    confirmedLabel: "You confirmed this agreement version",
    busyLabel: "Confirming current agreement",
    hint: "Confirms only the current immutable agreement version. Changed terms require a new version and both parties must confirm that version."
  },
  cancel: {
    label: "Cancel current ride",
    busyLabel: "Cancelling current ride",
    hint: "Requests cancellation of the active ride or negotiation. The server determines the resulting ride status."
  },
  followDriver: {
    label: "Follow driver on map",
    busyLabel: "Following driver on map",
    hint: "Resume centering the map on the latest shared driver location. Move the map to pause following."
  }
});

function getRiderActivityFieldProps(field, disabled) {
  const definition = ACTIVITY_FIELDS[field];
  if (!definition) throw new RangeError("Unknown Rider activity field.");
  return {
    accessibilityLabel: definition.accessibilityLabel,
    accessibilityHint: definition.accessibilityHint,
    accessibilityState: { disabled: Boolean(disabled) }
  };
}

function getRiderActivityActionProps(action, disabled, busy, confirmed = false) {
  const definition = ACTIVITY_ACTIONS[action];
  if (!definition) throw new RangeError("Unknown Rider activity action.");
  const label = confirmed && definition.confirmedLabel
    ? definition.confirmedLabel
    : busy
      ? definition.busyLabel
      : definition.label;
  return {
    accessibilityRole: "button",
    accessibilityLabel: label,
    accessibilityHint: definition.hint,
    accessibilityState: { disabled: Boolean(disabled), busy: Boolean(busy) }
  };
}

module.exports = {
  getRiderActivityActionProps,
  getRiderActivityFieldProps
};
