"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  getRiderProfileActionProps,
  getRiderProfileFieldProps,
  getRiderTabProps,
  getRiderThemeOptionProps
} = require("./riderProfileForm.js");

test("Rider theme options expose a labelled radio group state", () => {
  const selected = getRiderThemeOptionProps("dark", true);
  assert.equal(selected.accessibilityRole, "radio");
  assert.equal(selected.accessibilityLabel, "Use dark appearance");
  assert.deepEqual(selected.accessibilityState, { checked: true });

  const unselected = getRiderThemeOptionProps("system", false);
  assert.deepEqual(unselected.accessibilityState, { checked: false });
  assert.throws(() => getRiderThemeOptionProps("auto", false), /Unknown Rider theme option/);
});

test("Rider profile help input announces purpose and disabled state", () => {
  const enabled = getRiderProfileFieldProps("helpQuestion", false);
  assert.equal(enabled.accessibilityLabel, "Ask TurnOva help");
  assert.match(enabled.accessibilityHint, /approved TurnOva help content/);
  assert.match(enabled.accessibilityHint, /support escalation/);
  assert.deepEqual(enabled.accessibilityState, { disabled: false });

  const disabled = getRiderProfileFieldProps("helpQuestion", true);
  assert.deepEqual(disabled.accessibilityState, { disabled: true });
  assert.throws(() => getRiderProfileFieldProps("privateNotes", false), /Unknown Rider profile field/);
});

test("Rider help action announces approved content and busy state without implying ride changes", () => {
  const idle = getRiderProfileActionProps("askHelp", false, false);
  assert.equal(idle.accessibilityRole, "button");
  assert.equal(idle.accessibilityLabel, "Ask TurnOva assistant");
  assert.match(idle.accessibilityHint, /cannot book, charge or change a ride/);
  assert.deepEqual(idle.accessibilityState, { disabled: false, busy: false });

  const busy = getRiderProfileActionProps("askHelp", true, true);
  assert.equal(busy.accessibilityLabel, "Checking approved help");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
});

test("Rider sign-out action announces its in-flight state", () => {
  const idle = getRiderProfileActionProps("signOut", false, false);
  assert.equal(idle.accessibilityLabel, "Sign out");
  assert.match(idle.accessibilityHint, /returns to sign in/);
  assert.deepEqual(idle.accessibilityState, { disabled: false, busy: false });

  const busy = getRiderProfileActionProps("signOut", true, true);
  assert.equal(busy.accessibilityLabel, "Signing out");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
  assert.throws(() => getRiderProfileActionProps("payment", false, false), /Unknown Rider profile action/);
});

test("Rider navigation tabs expose selected and disabled state", () => {
  const activity = getRiderTabProps("Activity", true, false);
  assert.equal(activity.accessibilityRole, "tab");
  assert.equal(activity.accessibilityLabel, "Activity");
  assert.deepEqual(activity.accessibilityState, { selected: true, disabled: false });

  const blockedBook = getRiderTabProps("Book", false, true);
  assert.equal(blockedBook.accessibilityLabel, "Book rides");
  assert.deepEqual(blockedBook.accessibilityState, { selected: false, disabled: true });
  assert.throws(() => getRiderTabProps("Admin", false, false), /Unknown Rider navigation tab/);
});

test("Rider profile, help, theme and tab controls are wired to accessible helpers", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  for (const id of [
    "rider-profile-title",
    "rider-profile-name",
    "rider-profile-email",
    "rider-profile-role",
    "rider-help-question",
    "rider-help-submit",
    "rider-help-answer",
    "rider-sign-out",
    "rider-global-error",
    "rider-theme-",
    "rider-tab-"
  ]) {
    assert.ok(source.includes(id), "Missing Rider profile/navigation test ID fragment: " + id);
  }
  assert.match(source, /getRiderProfileFieldProps\("helpQuestion",helpBusy\)/);
  assert.match(source, /getRiderProfileActionProps\("askHelp"/);
  assert.match(source, /getRiderProfileActionProps\("signOut"/);
  assert.match(source, /getRiderThemeOptionProps\(option,themePreference===option\)/);
  assert.match(source, /getRiderTabProps\(item,tab===item/);
  assert.match(source, /accessibilityRole="radiogroup"/);
  assert.match(source, /accessibilityRole="tablist"/);
  assert.match(source, /accessibilityLiveRegion="polite" style={styles.muted}>\{helpAnswer\}/);
});

test("Rider help and sign-out handlers prevent overlapping submissions", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /if\(helpSubmissionInFlight\.current\)return;helpSubmissionInFlight\.current=true/);
  assert.match(source, /finally\{helpSubmissionInFlight\.current=false;setHelpBusy\(false\);\}/);
  assert.match(source, /if\(logoutInFlight\.current\)return;logoutInFlight\.current=true/);
  assert.match(source, /finally\{logoutInFlight\.current=false;setLogoutBusy\(false\);\}/);
});
