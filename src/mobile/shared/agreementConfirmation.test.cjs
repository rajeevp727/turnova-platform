const assert = require("node:assert/strict");
const test = require("node:test");
const { agreementConfirmationLabel } = require("./agreementConfirmation.js");

test("unconfirmed Rider and Driver parties remain pending", () => {
  assert.equal(agreementConfirmationLabel("Rider", false, null), "Rider: Pending");
  assert.equal(agreementConfirmationLabel("Driver", false, "2026-11-07T07:00:00Z"), "Driver: Pending");
});

test("confirmed parties display their recorded UTC timestamp in local format", () => {
  const timestamp = "2026-11-07T07:00:00.000Z";
  const expected = "Rider: Confirmed at " + new Date(timestamp).toLocaleString();

  assert.equal(agreementConfirmationLabel("Rider", true, timestamp), expected);
  assert.equal(
    agreementConfirmationLabel("Driver", true, timestamp),
    "Driver: Confirmed at " + new Date(timestamp).toLocaleString()
  );
});

test("legacy missing or invalid timestamps keep a safe confirmed label", () => {
  assert.equal(agreementConfirmationLabel("Rider", true, null), "Rider: Confirmed");
  assert.equal(agreementConfirmationLabel("Driver", true, undefined), "Driver: Confirmed");
  assert.equal(agreementConfirmationLabel("Driver", true, "not-a-timestamp"), "Driver: Confirmed");
});

test("unsupported agreement parties are rejected", () => {
  assert.throws(() => agreementConfirmationLabel("Union", true, null), TypeError);
});
