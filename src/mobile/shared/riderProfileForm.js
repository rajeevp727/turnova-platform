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

module.exports = {
  getRiderProfileActionProps,
  getRiderProfileFieldProps,
  getRiderTabProps,
  getRiderThemeOptionProps
};
