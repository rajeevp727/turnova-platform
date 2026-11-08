const assert = require("node:assert/strict");
const test = require("node:test");
const { applyRideStatusUpdate, findActiveRide } = require("./rideRecovery.js");

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


test("live ride status updates affect only the matching ride and preserve other fields", () => {
  const current = { rideId: "ride-1", status: "Accepted", pickup: "KPHB" };
  const updated = applyRideStatusUpdate(current, { rideId: "ride-1", status: "InProgress" });
  assert.deepEqual(updated, { rideId: "ride-1", status: "InProgress", pickup: "KPHB" });
  assert.equal(current.status, "Accepted");
  assert.equal(applyRideStatusUpdate(current, { rideId: "other", status: "Completed" }), current);
  assert.equal(applyRideStatusUpdate(null, { rideId: "ride-1", status: "Completed" }), null);
});

test("live terminal status events update the matching ride history projection", () => {
  const history = [
    { rideId: "ride-1", status: "InProgress" },
    { rideId: "ride-2", status: "Accepted" }
  ];
  const updated = history.map(ride => applyRideStatusUpdate(ride, { rideId: "ride-1", status: "Completed" }));
  assert.deepEqual(updated, [
    { rideId: "ride-1", status: "Completed" },
    { rideId: "ride-2", status: "Accepted" }
  ]);
});
