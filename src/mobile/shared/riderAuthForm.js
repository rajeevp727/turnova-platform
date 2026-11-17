"use strict";

const VALID_MODES = new Set(["login", "register"]);

function assertMode(mode) {
  if (!VALID_MODES.has(mode)) {
    throw new RangeError("Rider auth mode must be login or register.");
  }
}

function getRiderAuthFieldProps(field, mode = "login") {
  assertMode(mode);
  switch (field) {
    case "name":
      return {
        accessibilityLabel: "Full name",
        accessibilityHint: "Enter the name you want shown on your Rider account.",
        autoCapitalize: "words",
        autoCorrect: false,
        autoComplete: "name",
        textContentType: "name",
        importantForAutofill: "yes",
        returnKeyType: "next",
        blurOnSubmit: false
      };
    case "email":
      return {
        accessibilityLabel: "Email address",
        accessibilityHint: "Enter the email address for your TurnOva Rider account.",
        autoCapitalize: "none",
        autoCorrect: false,
        autoComplete: "email",
        textContentType: "emailAddress",
        keyboardType: "email-address",
        importantForAutofill: "yes",
        returnKeyType: "next",
        blurOnSubmit: false
      };
    case "password":
      return {
        accessibilityLabel: "Password",
        accessibilityHint: mode === "register"
          ? "Create a password for your TurnOva Rider account."
          : "Enter your TurnOva Rider account password.",
        autoCapitalize: "none",
        autoCorrect: false,
        autoComplete: mode === "register" ? "new-password" : "current-password",
        textContentType: mode === "register" ? "newPassword" : "password",
        importantForAutofill: "yes",
        secureTextEntry: true,
        returnKeyType: "go"
      };
    default:
      throw new RangeError("Unknown Rider auth field.");
  }
}

function getRiderAuthSubmitProps(mode, disabled, busy) {
  assertMode(mode);
  const action = mode === "login" ? "Sign in to TurnOva" : "Create your Rider account";
  return {
    accessibilityRole: "button",
    accessibilityLabel: busy
      ? (mode === "login" ? "Signing in to TurnOva" : "Creating your Rider account")
      : action,
    accessibilityHint: mode === "login"
      ? "Submits your email address and password securely."
      : "Creates your Rider account using the details provided.",
    accessibilityState: { disabled: Boolean(disabled), busy: Boolean(busy) }
  };
}

function getRiderAuthModeToggleProps(mode) {
  assertMode(mode);
  return {
    accessibilityRole: "button",
    accessibilityLabel: mode === "login" ? "Create a Rider account" : "Switch to sign in",
    accessibilityHint: mode === "login"
      ? "Opens the new Rider account form."
      : "Returns to the Rider sign-in form."
  };
}

function getRiderAuthErrorProps() {
  return {
    accessible: true,
    accessibilityRole: "alert",
    accessibilityLiveRegion: "assertive"
  };
}

module.exports = {
  getRiderAuthErrorProps,
  getRiderAuthFieldProps,
  getRiderAuthModeToggleProps,
  getRiderAuthSubmitProps
};
