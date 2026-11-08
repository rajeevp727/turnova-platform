const assert = require("node:assert/strict");
const test = require("node:test");
const { applyRideStatusUpdate, findActiveRide, shouldApplyRideStatusUpdate } = require("./rideRecovery.js");

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

test("ride status events apply only to the current ride while its screen effect is active", () => {
  assert.equal(shouldApplyRideStatusUpdate("ride-1", "ride-1", true), true);
  assert.equal(shouldApplyRideStatusUpdate("ride-1", "ride-2", true), false);
  assert.equal(shouldApplyRideStatusUpdate("ride-1", "ride-1", false), false);
  assert.equal(shouldApplyRideStatusUpdate("", "ride-1", true), false);
  assert.equal(shouldApplyRideStatusUpdate(null, "ride-1", true), false);
  assert.equal(shouldApplyRideStatusUpdate("ride-1", null, true), false);
});

test("unknown recovery roles fail explicitly", () => {
  assert.throws(() => findActiveRide([], "union"), /Unsupported ride recovery role/);
});

test("live ride status updates affect only the matching ride and preserve its immutable fields", () => {
  const current = {
    rideId: "ride-1",
    status: "Accepted",
    pickup: "KPHB",
    locationSharingActive: true,
    agreedAmount: 420
  };

  const updated = applyRideStatusUpdate(current, { rideId: "ride-1", status: "Completed" });
  assert.deepEqual(updated, {
    rideId: "ride-1",
    status: "Completed",
    pickup: "KPHB",
    locationSharingActive: false,
    agreedAmount: 420
  });
  assert.notEqual(updated, current);
  assert.equal(current.status, "Accepted");
  assert.equal(current.locationSharingActive, true);
  assert.equal(applyRideStatusUpdate(current, { rideId: "ride-2", status: "Completed" }), current);
  assert.equal(applyRideStatusUpdate(null, { rideId: "ride-1", status: "Completed" }), null);
  assert.equal(applyRideStatusUpdate(undefined, { rideId: "ride-1", status: "Completed" }), undefined);
});

test("live ride status updates keep location sharing aligned with foreground trip states", () => {
  const current = { rideId: "ride-1", status: "Completed", locationSharingActive: false };
  for (const status of ["Accepted", "Arrived", "InProgress"]) {
    assert.equal(applyRideStatusUpdate(current, { rideId: "ride-1", status }).locationSharingActive, true);
  }
  for (const status of ["Completed", "Cancelled", "Expired"]) {
    assert.equal(applyRideStatusUpdate(current, { rideId: "ride-1", status }).locationSharingActive, false);
  }
});

test("malformed live status updates are ignored without mutating the ride", () => {
  const current = { rideId: "ride-1", status: "Accepted" };
  assert.equal(applyRideStatusUpdate(current, { rideId: "", status: "Completed" }), current);
  assert.equal(applyRideStatusUpdate(current, { rideId: "ride-1", status: "" }), current);
  assert.equal(applyRideStatusUpdate(current, null), current);
});

test("live terminal status events update only the matching Driver history row", () => {
  const history = [
    { rideId: "ride-1", status: "InProgress" },
    { rideId: "ride-2", status: "Accepted" }
  ];
  const updated = history.map(ride => applyRideStatusUpdate(ride, { rideId: "ride-1", status: "Completed" }));
  assert.deepEqual(updated, [
    { rideId: "ride-1", status: "Completed" },
    { rideId: "ride-2", status: "Accepted" }
  ]);
  assert.equal(updated[1], history[1]);
  assert.equal(history[0].status, "InProgress");
});
