"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");
const { isStrictIsoTimestampWithOffset } = require("./riderTimestamp.js");

test("strict Rider timestamp validation accepts valid offset-qualified instants and leap days", () => {
  for (const value of [
    "2026-11-19T00:00:00Z",
    "2026-11-19T00:00:00.123456+05:30",
    "2024-02-29T23:59:59-05:00",
    "2026-11-19T00:00:00+00:00"
  ]) {
    assert.equal(isStrictIsoTimestampWithOffset(value), true, value);
  }
});

test("strict Rider timestamp validation rejects impossible calendar, clock and offset values", () => {
  for (const value of [
    "2026-02-30T00:00:00Z",
    "2026-04-31T12:00:00Z",
    "2025-02-29T00:00:00Z",
    "2026-13-01T00:00:00Z",
    "2026-11-19T24:00:00Z",
    "2026-11-19T00:60:00Z",
    "2026-11-19T00:00:60Z",
    "2026-11-19T00:00:00+24:00",
    "2026-11-19T00:00:00+05:60",
    "2026-11-19T00:00:00",
    "not-a-date",
    " 2026-11-19T00:00:00Z"
  ]) {
    assert.equal(isStrictIsoTimestampWithOffset(value), false, value);
  }
});

test("strict Rider timestamp validation rejects non-string values", () => {
  for (const value of [null, undefined, 0, new Date("2026-11-19T00:00:00Z"), {}]) {
    assert.equal(isStrictIsoTimestampWithOffset(value), false);
  }
});
