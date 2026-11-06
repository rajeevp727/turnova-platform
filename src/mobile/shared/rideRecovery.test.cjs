const assert = require("node:assert/strict");
const test = require("node:test");
const { findActiveRide } = require("./rideRecovery.js");

test("Rider recovery includes selected negotiation and active trip states", () => {
  const history = [
    { rideId: "completed", status: "Completed" },
    { rideId: "selected", status: "Negotiating" },
    { rideId: "older", status: "Accepted" }
  ];

  assert.equal(findActiveRide(history, "rider")?.rideId, "selected");
});

test("Driver recovery includes selected negotiation but excludes unassigned legacy offers", () => {
  const history = [
    { rideId: "legacy-offer", status: "Offered" },
    { rideId: "selected", status: "AgreementConfirmed" },
    { rideId: "completed", status: "Completed" }
  ];

  assert.equal(findActiveRide(history, "driver")?.rideId, "selected");
});

test("terminal-only or empty history clears stale active ride state", () => {
  const terminalHistory = [
    { rideId: "completed", status: "Completed" },
    { rideId: "cancelled", status: "Cancelled" },
    { rideId: "expired", status: "Expired" }
  ];

  assert.equal(findActiveRide(terminalHistory, "rider"), null);
  assert.equal(findActiveRide(terminalHistory, "driver"), null);
  assert.equal(findActiveRide([], "rider"), null);
});

test("unknown recovery roles fail explicitly", () => {
  assert.throws(() => findActiveRide([], "union"), /Unsupported ride recovery role/);
});
