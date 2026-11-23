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

const AMOUNT_PATTERN = /^(?:\d+(?:\.\d{0,2})?|\.\d{1,2})$/;

function parseRiderAgreementAmount(value) {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (!AMOUNT_PATTERN.test(normalized)) return null;
  const amount = Number(normalized);
  return Number.isFinite(amount) && amount > 0 ? amount : null;
}

function tryBeginRiderActivityAction(inFlight, key) {
  if (!inFlight || typeof inFlight !== "object" || !("current" in inFlight)) throw new TypeError("A Rider activity action lock is required.");
  if (typeof key !== "string" || !key || key !== key.trim() || key.length > 256 || /[\u0000-\u001f\u007f]/.test(key)) {
    throw new TypeError("A valid Rider activity action key is required.");
  }
  if (inFlight.current !== null) return false;
  inFlight.current = key;
  return true;
}

function finishRiderActivityAction(inFlight, key) {
  if (!inFlight || typeof inFlight !== "object" || !("current" in inFlight)) throw new TypeError("A Rider activity action lock is required.");
  if (typeof key !== "string" || !key || key !== key.trim() || key.length > 256 || /[\u0000-\u001f\u007f]/.test(key)) {
    throw new TypeError("A valid Rider activity action key is required.");
  }
  if (inFlight.current !== key) return false;
  inFlight.current = null;
  return true;
}

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
  parseRiderAgreementAmount,
  finishRiderActivityAction,
  tryBeginRiderActivityAction,
  getRiderActivityActionProps,
  getRiderActivityFieldProps
};
