"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const {
  passengerCountInputFromDraft,
  parsePassengerCountInput,
  normalizeRiderTripIntentDraft,
  ownsRiderTripIntentRequest,
  shouldApplyRiderTripIntentResponse
} = require("./tripIntentDraft");

test("a missing or invalid AI passenger count clears the stale form value", () => {
  for (const value of [null, undefined, 0, 7, 1.5, "2"]) {
    assert.equal(passengerCountInputFromDraft(value), "");
  }
});

test("an explicitly parsed AI passenger count is copied only within the supported range", () => {
  for (let value = 1; value <= 6; value += 1) {
    assert.equal(passengerCountInputFromDraft(value), String(value));
  }
});

test("manual passenger input accepts only one integer from one through six", () => {
  for (let value = 1; value <= 6; value += 1) {
    assert.equal(parsePassengerCountInput(String(value)), value);
  }
  for (const value of ["", " ", "0", "7", "10", "1.5", "-1", "2 people", "abc"]) {
    assert.equal(parsePassengerCountInput(value), null, value);
  }
});

test("AI-01-RIDER-DRAFT-RESPONSE-001: accept only a valid Cab draft requiring explicit Rider confirmation", () => {
  const valid = {
    pickup: "Nizampet",
    dropoff: "HITEC City",
    serviceClass: "Cab",
    passengerCount: 2,
    needsClarification: false,
    missingFields: [],
    requiresUserConfirmation: true,
    source: "deterministic-fallback",
    providerMetadata: { ignored: true }
  };
  assert.deepEqual(normalizeRiderTripIntentDraft(valid), {
    pickup: "Nizampet",
    dropoff: "HITEC City",
    serviceClass: "Cab",
    passengerCount: 2,
    needsClarification: false,
    missingFields: [],
    requiresUserConfirmation: true,
    source: "deterministic-fallback"
  });
  assert.equal(normalizeRiderTripIntentDraft({ ...valid, source: "provider" }).source, "provider");
});

test("AI-01-RIDER-DRAFT-RESPONSE-002: reject malformed, inconsistent or non-Cab assistant drafts", () => {
  const valid = {
    pickup: "Nizampet",
    dropoff: "HITEC City",
    serviceClass: "Cab",
    passengerCount: 2,
    needsClarification: false,
    missingFields: [],
    requiresUserConfirmation: true,
    source: "deterministic-fallback"
  };
  const invalid = [
    null, [], "draft",
    { ...valid, pickup: "" },
    { ...valid, pickup: " Nizampet " },
    { ...valid, pickup: "x".repeat(161) },
    { ...valid, dropoff: {} },
    { ...valid, serviceClass: "Bike" },
    { ...valid, passengerCount: 7 },
    { ...valid, passengerCount: null },
    { ...valid, missingFields: ["passengerCount"] },
    { ...valid, needsClarification: true },
    { ...valid, requiresUserConfirmation: false },
    { ...valid, source: "unknown-provider" },
    { ...valid, missingFields: ["pickup", "pickup"] }
  ];
  for (const response of invalid) assert.equal(normalizeRiderTripIntentDraft(response), null);
  const partial = normalizeRiderTripIntentDraft({
    ...valid,
    pickup: null,
    passengerCount: null,
    needsClarification: true,
    missingFields: ["pickup", "passengerCount"]
  });
  assert.deepEqual(partial?.missingFields, ["pickup", "passengerCount"]);
});

test("AI-01-RIDER-DRAFT-ASYNC-001: only the latest request in the same authenticated session owns the draft request", () => {
  assert.equal(ownsRiderTripIntentRequest(3, 3, 8, 8), true);
  assert.equal(ownsRiderTripIntentRequest(2, 3, 8, 8), false);
  assert.equal(ownsRiderTripIntentRequest(3, 3, 7, 8), false);
  assert.equal(ownsRiderTripIntentRequest(0, 3, 8, 8), false);
  assert.equal(ownsRiderTripIntentRequest(3.5, 3, 8, 8), false);
});

test("AI-01-RIDER-DRAFT-ASYNC-002: a draft cannot overwrite trip fields edited while the request was pending", () => {
  assert.equal(shouldApplyRiderTripIntentResponse(4, 4, 8, 8, 12, 12), true);
  assert.equal(shouldApplyRiderTripIntentResponse(4, 4, 8, 8, 12, 13), false);
  assert.equal(shouldApplyRiderTripIntentResponse(3, 4, 8, 8, 12, 12), false);
  assert.equal(shouldApplyRiderTripIntentResponse(4, 4, 8, 9, 12, 12), false);
  assert.equal(shouldApplyRiderTripIntentResponse(4, 4, 8, 8, -1, -1), false);
});

test("AI-01-RIDER-DRAFT-ASYNC-003: Rider screen snapshots prompt and fences session, form edits and stale finalizers", () => {
  const app = fs.readFileSync(path.join(__dirname, "..", "rider-app", "App.tsx"), "utf8");
  assert.match(app, /const riderTripIntentRequestSequence=useRef\(0\)/);
  assert.match(app, /const riderTripFormRevision=useRef\(0\)/);
  assert.match(app, /const requestText=tripIntentText/);
  assert.match(app, /const requestSessionEpoch=sessionEpoch\.current/);
  assert.match(app, /normalizeRiderTripIntentDraft\(response\)/);
  assert.match(app, /shouldApplyRiderTripIntentResponse\(/);
  assert.match(app, /if\(ownsRequest\(\)\)setTripIntentBusy\(false\)/);
  assert.match(app, /riderTripIntentRequestSequence\.current \+= 1/);
  assert.match(app, /riderTripFormRevision\.current \+= 1/);
  for (const field of ["tripIntentText", "pickup", "dropoff", "stopsText", "passengers"]) {
    assert.match(app, new RegExp("markRiderTripFormEdited\\(\\);set" + field[0].toUpperCase() + field.slice(1) + "\\("));
  }
});
