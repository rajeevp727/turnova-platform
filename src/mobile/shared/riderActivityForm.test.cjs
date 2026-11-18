"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  getRiderActivityActionProps,
  getRiderActivityFieldProps
} = require("./riderActivityForm.js");

test("Rider agreement amount input explains direct negotiation and exposes disabled state", () => {
  const enabled = getRiderActivityFieldProps("agreementAmount", false);
  assert.equal(enabled.accessibilityLabel, "Proposed or counter fare amount");
  assert.match(enabled.accessibilityHint, /directly to the selected Driver/);
  assert.match(enabled.accessibilityHint, /does not estimate, set or collect fares/);
  assert.deepEqual(enabled.accessibilityState, { disabled: false });

  const disabled = getRiderActivityFieldProps("agreementAmount", true);
  assert.deepEqual(disabled.accessibilityState, { disabled: true });
});

test("Rider proposal action announces busy state and preserves direct fare negotiation", () => {
  const idle = getRiderActivityActionProps("propose", false, false);
  assert.equal(idle.accessibilityRole, "button");
  assert.equal(idle.accessibilityLabel, "Propose or counter fare");
  assert.match(idle.accessibilityHint, /Both parties must confirm the current agreement version/);
  assert.match(idle.accessibilityHint, /does not collect payment/);
  assert.deepEqual(idle.accessibilityState, { disabled: false, busy: false });

  const busy = getRiderActivityActionProps("propose", true, true);
  assert.equal(busy.accessibilityLabel, "Submitting fare proposal");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
});

test("Rider agreement confirmation distinguishes the current version and already-confirmed state", () => {
  const pending = getRiderActivityActionProps("confirm", false, false, false);
  assert.equal(pending.accessibilityLabel, "Confirm current agreement version");
  assert.match(pending.accessibilityHint, /Changed terms require a new version/);
  assert.deepEqual(pending.accessibilityState, { disabled: false, busy: false });

  const confirmed = getRiderActivityActionProps("confirm", true, false, true);
  assert.equal(confirmed.accessibilityLabel, "You confirmed this agreement version");
  assert.deepEqual(confirmed.accessibilityState, { disabled: true, busy: false });
});

test("Rider cancellation remains a server-owned request and announces in-flight state", () => {
  const idle = getRiderActivityActionProps("cancel", false, false);
  assert.equal(idle.accessibilityLabel, "Cancel current ride");
  assert.match(idle.accessibilityHint, /server determines the resulting ride status/);

  const busy = getRiderActivityActionProps("cancel", true, true);
  assert.equal(busy.accessibilityLabel, "Cancelling current ride");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
});

test("Rider map follow control announces resuming and pausing location following", () => {
  const props = getRiderActivityActionProps("followDriver", false, false);
  assert.equal(props.accessibilityLabel, "Follow driver on map");
  assert.match(props.accessibilityHint, /latest shared driver location/);
  assert.match(props.accessibilityHint, /pause following/);
});

test("Rider activity helpers reject unknown fields and actions", () => {
  assert.throws(() => getRiderActivityFieldProps("pickup", false), /Unknown Rider activity field/);
  assert.throws(() => getRiderActivityActionProps("pay", false, false), /Unknown Rider activity action/);
});

test("Rider Activity screen wires agreement, status, cancellation, tracking and history semantics", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  for (const id of [
    "rider-activity-title",
    "rider-activity-ride-status",
    "rider-activity-status-message",
    "rider-agreement-amount",
    "rider-agreement-propose",
    "rider-agreement-confirm",
    "rider-follow-driver",
    "rider-cancel-ride"
  ]) {
    assert.ok(source.includes('testID="' + id + '"'), "Missing Rider Activity testID: " + id);
  }
  assert.match(source, /getRiderActivityFieldProps\("agreementAmount",proposalSubmitting\)/);
  assert.match(source, /getRiderActivityActionProps\("propose"/);
  assert.match(source, /getRiderActivityActionProps\("confirm"/);
  assert.match(source, /getRiderActivityActionProps\("cancel",loading,loading\)/);
  assert.match(source, /getRiderActivityActionProps\("followDriver",false,false\)/);
  assert.ok(source.includes("rider-agreement-version-"));
  assert.ok(source.includes("rider-history-"));
  assert.match(source, /accessibilityLiveRegion="polite"/);
});
