"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const { finishRiderBookingAttempt, tryBeginRiderBookingAttempt } = require("./riderBookingAttempt.js");

test("Rider booking attempt is single-flight and only its owner can release it", () => {
  const lock = { current: null };
  assert.equal(tryBeginRiderBookingAttempt(lock, "booking-a"), true);
  assert.equal(tryBeginRiderBookingAttempt(lock, "booking-b"), false);
  assert.equal(finishRiderBookingAttempt(lock, "booking-b"), false);
  assert.equal(lock.current, "booking-a");
  assert.equal(finishRiderBookingAttempt(lock, "booking-a"), true);
  assert.equal(lock.current, null);
});

test("Rider session reset permits a new booking owner and fences the stale finalizer", () => {
  const lock = { current: null };
  assert.equal(tryBeginRiderBookingAttempt(lock, "old-session-key"), true);
  lock.current = null; // clearRiderSessionState invalidates the old owner.
  assert.equal(tryBeginRiderBookingAttempt(lock, "new-session-key"), true);
  assert.equal(finishRiderBookingAttempt(lock, "old-session-key"), false);
  assert.equal(lock.current, "new-session-key");
  assert.equal(finishRiderBookingAttempt(lock, "new-session-key"), true);
  assert.equal(lock.current, null);
});

test("Rider booking lock rejects malformed owners and lock references", () => {
  const lock = { current: null };
  for (const owner of ["", " padded ", "x".repeat(257), "bad\\r\\nowner"]) {
    assert.throws(() => tryBeginRiderBookingAttempt(lock, owner), /valid Rider booking attempt owner/);
  }
  assert.throws(() => tryBeginRiderBookingAttempt(null, "booking-a"), /lock is required/);
  assert.throws(() => finishRiderBookingAttempt({}, "booking-a"), /lock is required/);
  assert.equal(lock.current, null);
});

test("Rider booking handler fences session state and releases only its booking owner", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /const renderedSessionEpoch=sessionEpoch\\.current/);
  assert.match(source, /const bookingInFlight=useRef<string\\|null>\\(null\\)/);

  const resetStart = source.indexOf("const clearRiderSessionState");
  const resetEnd = source.indexOf("useEffect", resetStart);
  assert.ok(resetStart >= 0 && resetEnd > resetStart);
  const resetSource = source.slice(resetStart, resetEnd);
  assert.match(resetSource, /bookingInFlight\\.current=null/);

  const start = source.indexOf("const requestSelectedDriver=async()=>{");
  const end = source.indexOf("const loadAgreementHistory", start);
  assert.ok(start >= 0 && end > start);
  const handler = source.slice(start, end);
  for (const expected of [
    "const requestEpoch=renderedSessionEpoch;",
    "isSessionEpochCurrent(sessionEpoch,authenticatedSessionEpoch,requestEpoch)",
    "tryBeginRiderBookingAttempt(bookingInFlight,requestKey)",
    "finishRiderBookingAttempt(bookingInFlight,requestKey)",
    "if(!isCurrent())return;",
    "&&isCurrent())setLoading(false)"
  ]) assert.ok(handler.includes(expected), "Missing booking session guard: " + expected);
});
