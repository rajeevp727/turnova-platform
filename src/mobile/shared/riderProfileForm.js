"use strict";

const THEME_OPTIONS = Object.freeze({
  system: { label: "Use system appearance", hint: "Follow the device appearance setting." },
  light: { label: "Use light appearance", hint: "Use the light app theme." },
  dark: { label: "Use dark appearance", hint: "Use the dark app theme." }
});

const PROFILE_FIELDS = Object.freeze({
  helpQuestion: {
    accessibilityLabel: "Ask TurnOva help",
    accessibilityHint: "Ask about your account or ride. Answers use approved TurnOva help content and may require support escalation."
  }
});

const PROFILE_ACTIONS = Object.freeze({
  askHelp: {
    label: "Ask TurnOva assistant",
    busyLabel: "Checking approved help",
    hint: "Uses approved TurnOva help content. It cannot book, charge or change a ride. Support escalation may be required."
  },
  signOut: {
    label: "Sign out",
    busyLabel: "Signing out",
    hint: "Ends this Rider session and returns to sign in."
  }
});

const RIDER_TABS = Object.freeze({
  Book: { label: "Book rides", hint: "Open Cab ride discovery and selected-driver booking." },
  Activity: { label: "Activity", hint: "View the selected ride, agreement and trip history." },
  Offers: { label: "Offers", hint: "View the fare negotiation information for your ride." },
  Profile: { label: "Profile", hint: "Open Rider profile, help and appearance settings." }
});

const HELP_SOURCE_IDS = new Set([
  "state-negotiating",
  "state-agreement-confirmed",
  "state-accepted",
  "state-arrived",
  "state-in-progress",
  "state-completed",
  "state-cancelled",
  "state-expired",
  "fare-negotiation",
  "agreement-version",
  "direct-payment",
  "driver-photo",
  "driver-selection",
  "reservation",
  "queue-status",
  "availability-status",
  "unsupported"
]);

const HELP_SOURCE_LABELS = Object.freeze({
  "approved-content": "Approved in-repository help content",
  "provider-approved-content": "AI-selected approved help content",
  "deterministic-fallback": "Deterministic fallback from approved help content"
});

function normalizeRiderHelpResponse(value) {
  const isRecord = item => item !== null && typeof item === "object" && !Array.isArray(item);
  if (!isRecord(value) ||
      typeof value.answer !== "string" ||
      value.answer.length === 0 ||
      value.answer.length > 1000 ||
      value.answer !== value.answer.trim() ||
      /[\u0000-\u001f\u007f-\u009f]/.test(value.answer) ||
      typeof value.sourceId !== "string" ||
      !HELP_SOURCE_IDS.has(value.sourceId) ||
      typeof value.escalationRequired !== "boolean" ||
      typeof value.source !== "string" ||
      !Object.prototype.hasOwnProperty.call(HELP_SOURCE_LABELS, value.source)) return null;

  // The API's unsupported entry is the only current help response that escalates.
  if (value.sourceId === "unsupported") {
    if (!value.escalationRequired || value.source !== "deterministic-fallback") return null;
  } else if (value.escalationRequired) {
    return null;
  }

  // Project only the approved response contract; ignore transport/provider metadata.
  return Object.freeze({
    answer: value.answer,
    sourceId: value.sourceId,
    escalationRequired: value.escalationRequired,
    source: value.source
  });
}

function getRiderHelpSourceLabel(source) {
  const label = HELP_SOURCE_LABELS[source];
  if (!label) throw new RangeError("Unknown Rider help response source.");
  return label;
}

function getRiderThemeOptionProps(option, selected) {
  const definition = THEME_OPTIONS[option];
  if (!definition) throw new RangeError("Unknown Rider theme option.");
  return {
    accessibilityRole: "radio",
    accessibilityLabel: definition.label,
    accessibilityHint: definition.hint,
    accessibilityState: { checked: Boolean(selected) }
  };
}

function getRiderProfileFieldProps(field, disabled) {
  const definition = PROFILE_FIELDS[field];
  if (!definition) throw new RangeError("Unknown Rider profile field.");
  return {
    accessibilityLabel: definition.accessibilityLabel,
    accessibilityHint: definition.accessibilityHint,
    accessibilityState: { disabled: Boolean(disabled) }
  };
}

function getRiderProfileActionProps(action, disabled, busy) {
  const definition = PROFILE_ACTIONS[action];
  if (!definition) throw new RangeError("Unknown Rider profile action.");
  return {
    accessibilityRole: "button",
    accessibilityLabel: busy ? definition.busyLabel : definition.label,
    accessibilityHint: definition.hint,
    accessibilityState: { disabled: Boolean(disabled), busy: Boolean(busy) }
  };
}

function getRiderTabProps(tab, selected, disabled) {
  const definition = RIDER_TABS[tab];
  if (!definition) throw new RangeError("Unknown Rider navigation tab.");
  return {
    accessibilityRole: "tab",
    accessibilityLabel: definition.label,
    accessibilityHint: definition.hint,
    accessibilityState: { selected: Boolean(selected), disabled: Boolean(disabled) }
  };
}

function shouldApplyRiderProfileRequestResponse(requestSequence, currentSequence, requestSessionEpoch, currentSessionEpoch) {
  return Number.isSafeInteger(requestSequence) && requestSequence > 0 &&
    Number.isSafeInteger(currentSequence) && requestSequence === currentSequence &&
    Number.isSafeInteger(requestSessionEpoch) && requestSessionEpoch >= 0 &&
    Number.isSafeInteger(currentSessionEpoch) && requestSessionEpoch === currentSessionEpoch;
}

module.exports = {
  normalizeRiderHelpResponse,
  getRiderHelpSourceLabel,
  shouldApplyRiderProfileRequestResponse,
  getRiderProfileActionProps,
  getRiderProfileFieldProps,
  getRiderTabProps,
  getRiderThemeOptionProps
};
