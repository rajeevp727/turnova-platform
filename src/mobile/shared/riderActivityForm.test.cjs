"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  finishRiderActivityAction,
  getRiderActivityActionProps,
  parseRiderAgreementAmount,
  getRiderActivityFieldProps,
  tryBeginRiderActivityAction
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


test("Rider Activity mutation guard allows one action and only its owner can release it", () => {
  const lock = { current: null };
  assert.equal(tryBeginRiderActivityAction(lock, "cancel:ride-1"), true);
  assert.equal(tryBeginRiderActivityAction(lock, "cancel:ride-1"), false);
  assert.equal(tryBeginRiderActivityAction(lock, "confirm:ride-1:2"), false);
  assert.equal(finishRiderActivityAction(lock, "confirm:ride-1:2"), false);
  assert.equal(lock.current, "cancel:ride-1");
  assert.equal(finishRiderActivityAction(lock, "cancel:ride-1"), true);
  assert.equal(lock.current, null);
  assert.equal(tryBeginRiderActivityAction(lock, "confirm:ride-1:2"), true);
  assert.equal(finishRiderActivityAction(lock, "confirm:ride-1:2"), true);
});

test("Rider Activity mutation guard rejects malformed lock keys", () => {
  const lock = { current: null };
  assert.throws(() => tryBeginRiderActivityAction(lock, ""), /valid Rider activity action key/);
  assert.throws(() => tryBeginRiderActivityAction(lock, " bad "), /valid Rider activity action key/);
  assert.throws(() => tryBeginRiderActivityAction(lock, "x".repeat(257)), /valid Rider activity action key/);
  assert.throws(() => tryBeginRiderActivityAction(lock, "bad\r\nkey"), /valid Rider activity action key/);
  assert.throws(() => tryBeginRiderActivityAction(null, "cancel:ride-1"), /lock is required/);
  assert.equal(lock.current, null);
});

test("a stale Rider Activity finalizer cannot release a same-ride/version action after session reset", () => {
  const lock = { current: null };
  const previousOwner = "confirm:4:1:ride-1:2";
  const currentOwner = "confirm:6:2:ride-1:2";

  assert.equal(tryBeginRiderActivityAction(lock, previousOwner), true);
  lock.current = null; // Session reset invalidates the previous action owner.
  assert.equal(tryBeginRiderActivityAction(lock, currentOwner), true);
  assert.equal(finishRiderActivityAction(lock, previousOwner), false);
  assert.equal(lock.current, currentOwner);
  assert.equal(finishRiderActivityAction(lock, currentOwner), true);
  assert.equal(lock.current, null);
});

test("Rider cancellation is single-flight and ignores stale ride/session responses", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const start = source.indexOf("const cancelRide=async()=>");
  const end = source.indexOf("const requestSelectedDriver", start);
  assert.ok(start >= 0 && end > start);
  const handler = source.slice(start, end);
  for (const expected of [
    "const actionKey=`cancel:${requestEpoch}:${++activityActionSequence.current}:${rideId}`",
    "tryBeginRiderActivityAction(activityActionInFlight,actionKey)",
    "currentRideIdRef.current===rideId",
    "isSessionEpochCurrent(sessionEpoch,authenticatedSessionEpoch,requestEpoch)",
    "finishRiderActivityAction(activityActionInFlight,actionKey)",
    "setCancelBusy(false)",
    "isRiderCancellationResponseValid(updated,rideId)"
  ]) assert.ok(handler.includes(expected), "Missing cancellation guard: " + expected);
});

test("Rider agreement confirmation is single-flight per ride/version and refreshes authoritative history", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const start = source.indexOf("const confirmAgreement=async()=>");
  const end = source.indexOf("useEffect(()=>{if(!user", start);
  assert.ok(start >= 0 && end > start);
  const handler = source.slice(start, end);
  for (const expected of [
    "const actionKey=`confirm:${requestEpoch}:${++activityActionSequence.current}:${rideId}:${current.version}`",
    "current.riderConfirmed",
    "tryBeginRiderActivityAction(activityActionInFlight,actionKey)",
    "loadAgreementHistory(rideId,isCurrent)",
    "finishRiderActivityAction(activityActionInFlight,actionKey)",
    "setConfirmBusyVersion(null)"
  ]) assert.ok(handler.includes(expected), "Missing confirmation guard: " + expected);
});

test("Rider proposal, confirmation and cancellation serialize shared ride mutations", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  for (const expected of [
    "if(!rideId||!proposalUserId||proposalSubmissionInFlight.current||activityActionInFlight.current)return;",
    "if(!rideId||proposalSubmissionInFlight.current)return;",
    "if(!rideId||!current||current.riderConfirmed||proposalSubmissionInFlight.current)return;",
    "const activityActionBusy=cancelBusy||confirmBusyVersion!==null||proposalSubmitting;",
    'getRiderActivityActionProps("confirm",loading||activityActionBusy',
    'getRiderActivityActionProps("cancel",loading||activityActionBusy,cancelBusy)',
    'getRiderActivityFieldProps("agreementAmount",activityActionBusy)',
    "editable={!activityActionBusy}"
  ]) assert.ok(source.includes(expected), "Missing shared action serialization: " + expected);
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
  assert.ok(source.includes('getRiderActivityFieldProps("agreementAmount",activityActionBusy)'));
  assert.match(source, /getRiderActivityActionProps\("propose"/);
  assert.match(source, /getRiderActivityActionProps\("confirm"/);
  assert.ok(source.includes('getRiderActivityActionProps("cancel",loading||activityActionBusy,cancelBusy)'));
  assert.match(source, /getRiderActivityActionProps\("followDriver",false,false\)/);
  assert.ok(source.includes("rider-agreement-version-"));
  assert.ok(source.includes("rider-history-"));
  assert.match(source, /accessibilityLiveRegion="polite"/);
});


test("RIDER-AGREEMENT-INPUT-001: accept positive plain-decimal INR amounts with at most two fractional digits", () => {
  const accepted = new Map([
    ["450", 450],
    ["450.0", 450],
    ["450.00", 450],
    ["0.01", 0.01],
    [".5", 0.5],
    ["450.", 450],
    [" 450.25 ", 450.25],
    ["0007.50", 7.5]
  ]);
  for (const [input, expected] of accepted) {
    assert.equal(parseRiderAgreementAmount(input), expected, input);
  }
});

test("RIDER-AGREEMENT-INPUT-001: reject zero, negative, signed, exponent, hex, locale and over-precision amounts", () => {
  const rejected = [
    null, undefined, 450, {}, "", " ", "0", "0.00", "-1", "+1",
    "1.001", "1.234", "1e3", "0x10", "Infinity", "NaN",
    "1,000", "₹450", ".", "--1", "1 0", "1\n0",
    "9".repeat(400)
  ];
  for (const input of rejected) {
    assert.equal(parseRiderAgreementAmount(input), null, String(input));
  }
});

test("RIDER-AGREEMENT-INPUT-001: App validates proposal text before creating its durable idempotency attempt", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const start = source.indexOf("const proposeAgreement=async()=>{");
  const end = source.indexOf("const confirmAgreement=async()=>{", start);
  assert.ok(start >= 0 && end > start);
  const handler = source.slice(start, end);
  const parse = handler.indexOf("parseRiderAgreementAmount(agreementAmount)");
  const persist = handler.indexOf("getOrCreatePersistentProposalAttempt(");
  assert.ok(parse >= 0 && persist > parse);
  assert.ok(handler.includes('if(amount===null){setError("Enter a positive INR amount using no more than two decimal places.");return;}'));
});
