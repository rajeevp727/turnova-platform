"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");
const {
  passengerCountInputFromDraft,
  parsePassengerCountInput
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
