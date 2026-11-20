"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  getRiderAuthErrorProps,
  getRiderAuthFieldProps,
  getRiderAuthModeToggleProps,
  getRiderAuthSubmitProps,
  finishRiderAuthSubmission,
  tryBeginRiderAuthSubmission
} = require("./riderAuthForm.js");

test("Rider registration name field exposes native name autofill and next-key behavior", () => {
  const props = getRiderAuthFieldProps("name", "register");
  assert.equal(props.accessibilityLabel, "Full name");
  assert.equal(props.autoComplete, "name");
  assert.equal(props.textContentType, "name");
  assert.equal(props.importantForAutofill, "yes");
  assert.equal(props.returnKeyType, "next");
  assert.equal(props.blurOnSubmit, false);
});

test("Rider email field disables capitalization/correction and exposes email autofill", () => {
  const props = getRiderAuthFieldProps("email");
  assert.equal(props.accessibilityLabel, "Email address");
  assert.equal(props.keyboardType, "email-address");
  assert.equal(props.autoComplete, "email");
  assert.equal(props.textContentType, "emailAddress");
  assert.equal(props.autoCapitalize, "none");
  assert.equal(props.autoCorrect, false);
  assert.equal(props.importantForAutofill, "yes");
  assert.equal(props.returnKeyType, "next");
  assert.equal(props.blurOnSubmit, false);
});

test("Rider login password uses current-password autofill without capitalization", () => {
  const props = getRiderAuthFieldProps("password", "login");
  assert.equal(props.accessibilityLabel, "Password");
  assert.equal(props.autoComplete, "current-password");
  assert.equal(props.textContentType, "password");
  assert.equal(props.secureTextEntry, true);
  assert.equal(props.autoCapitalize, "none");
  assert.equal(props.autoCorrect, false);
  assert.equal(props.returnKeyType, "go");
});

test("Rider registration password uses new-password autofill", () => {
  const props = getRiderAuthFieldProps("password", "register");
  assert.equal(props.autoComplete, "new-password");
  assert.equal(props.textContentType, "newPassword");
  assert.match(props.accessibilityHint, /Create a password/);
});

test("Rider submit action announces busy and disabled states", () => {
  const idle = getRiderAuthSubmitProps("login", false, false);
  assert.equal(idle.accessibilityRole, "button");
  assert.equal(idle.accessibilityLabel, "Sign in to TurnOva");
  assert.equal(idle.accessibilityHint, "Submits your email address and password to sign in.");
  assert.deepEqual(idle.accessibilityState, { disabled: false, busy: false });

  const busy = getRiderAuthSubmitProps("register", true, true);
  assert.equal(busy.accessibilityLabel, "Creating your Rider account");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
});

test("Rider auth submission gate prevents overlapping login/register dispatch and reopens after completion", () => {
  const gate = { current: false };
  assert.equal(tryBeginRiderAuthSubmission(gate), true);
  assert.equal(gate.current, true);
  assert.equal(tryBeginRiderAuthSubmission(gate), false);
  assert.equal(gate.current, true);
  finishRiderAuthSubmission(gate);
  assert.equal(gate.current, false);
  assert.equal(tryBeginRiderAuthSubmission(gate), true);
  finishRiderAuthSubmission(gate);
});

test("Rider auth mode toggle announces the destination action", () => {
  const register = getRiderAuthModeToggleProps("login");
  assert.equal(register.accessibilityLabel, "Create a Rider account");
  assert.match(register.accessibilityHint, /new Rider account/);

  const login = getRiderAuthModeToggleProps("register");
  assert.equal(login.accessibilityLabel, "Switch to sign in");
  assert.match(login.accessibilityHint, /sign-in form/);
});

test("Rider auth errors use an assertive alert announcement", () => {
  assert.deepEqual(getRiderAuthErrorProps(), {
    accessible: true,
    accessibilityRole: "alert",
    accessibilityLiveRegion: "assertive"
  });
});

test("Rider auth field helper rejects unknown fields and modes", () => {
  assert.throws(() => getRiderAuthFieldProps("phone"), /Unknown Rider auth field/);
  assert.throws(() => getRiderAuthFieldProps("email", "other"), /mode must be login or register/);
});

test("Rider demo entry is accessible and clearly read-only", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /testID="rider-demo-login"/);
  assert.match(source, /Continue as Demo Rider/);
  assert.match(source, /Demo mode is read-only/);
  assert.match(source, /real booking, payment and live tracking are unavailable/);
  assert.match(source, /if\(!user&&!demoMode\)return/);
});
