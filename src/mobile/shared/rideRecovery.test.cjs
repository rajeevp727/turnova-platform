const assert = require("node:assert/strict");
const test = require("node:test");
const { applyRideStatusUpdate, findActiveRide, isRiderRideDetailsResponseValid, isRiderRideHistoryShapeValid, shouldApplyRideHistoryResponse, shouldApplyRideStatusUpdate } = require("./rideRecovery.js");

test("Rider ride-history shape accepts an empty list and uniquely identified records", () => {
  assert.equal(isRiderRideHistoryShapeValid([]), true);
  assert.equal(isRiderRideHistoryShapeValid([
    { rideId: "ride-1", status: "Completed" },
    { rideId: "ride-2", status: "FutureServerStatus" }
  ]), true);
});

test("Rider ride-history shape rejects malformed rows, invalid IDs and duplicate IDs", () => {
  for (const invalid of [
    null,
    {},
    "not-an-array",
    [null],
    [42],
    [[]],
    [{}],
    [{ rideId: "" }],
    [{ rideId: " ride-1" }],
    [{ rideId: "ride-1 " }],
    [{ rideId: "ride-1" }, { rideId: "ride-1" }]
  ]) {
    assert.equal(isRiderRideHistoryShapeValid(invalid), false);
  }
});

test("Rider ride-details response must match the active history identity", () => {
  assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-1", status: "InProgress" }, "ride-1"), true);
  assert.equal(isRiderRideDetailsResponseValid({
    rideId: "ride-1",
    requestedAt: "2026-11-19T00:00:00Z",
    locationCapturedAtUtc: "2026-11-19T00:00:01Z"
  }, "ride-1"), true);
  for (const field of ["requestedAt", "acceptedAt", "negotiationExpiresAtUtc", "locationUpdatedAt", "locationCapturedAtUtc", "driverLocationCapturedAtUtc"]) {
    assert.equal(isRiderRideDetailsResponseValid({
      rideId: "ride-1",
      [field]: "2026-11-31T00:00:00Z"
    }, "ride-1"), false, field);
  }
  assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-1", status: "FutureServerStatus" }, "ride-1"), true);
  assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-2", status: "Completed" }, "ride-1"), false);
  assert.equal(isRiderRideDetailsResponseValid({ status: "Completed" }, "ride-1"), false);
  assert.equal(isRiderRideDetailsResponseValid([], "ride-1"), false);
  assert.equal(isRiderRideDetailsResponseValid(null, "ride-1"), false);
  assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-1" }, " ride-1"), false);
});

test("only the newest Rider history hydration request may apply its response", () => {
  assert.equal(shouldApplyRideHistoryResponse(1, 1), true);
  assert.equal(shouldApplyRideHistoryResponse(1, 2), false);
  assert.equal(shouldApplyRideHistoryResponse(2, 2), true);
  assert.equal(shouldApplyRideHistoryResponse(0, 0), false);
  assert.equal(shouldApplyRideHistoryResponse(-1, -1), false);
  assert.equal(shouldApplyRideHistoryResponse(Number.NaN, Number.NaN), false);
  assert.equal(shouldApplyRideHistoryResponse(Number.MAX_SAFE_INTEGER + 1, Number.MAX_SAFE_INTEGER + 1), false);
});

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

test("Rider recovery retains unknown future lifecycle records until the server confirms a known terminal state", () => {
  const unknown = { rideId: "future-status", status: "FutureServerStatus" };
  assert.equal(findActiveRide([unknown], "rider"), unknown);
});

test("Rider recovery prefers a known active ride over an unknown future history value", () => {
  const unknown = { rideId: "future-status", status: "FutureServerStatus" };
  const active = { rideId: "active", status: "InProgress" };
  assert.equal(findActiveRide([unknown, active], "rider"), active);
});

test("Rider recovery fails closed for a malformed lifecycle value", () => {
  const malformed = { rideId: "missing-status" };
  assert.equal(findActiveRide([malformed], "rider"), malformed);
});

test("Driver legacy Offered exclusion is unchanged by Rider unknown-status fallback", () => {
  const legacyOffer = { rideId: "legacy-offer", status: "Offered" };
  const unknown = { rideId: "future-status", status: "FutureServerStatus" };
  assert.equal(findActiveRide([legacyOffer], "driver"), null);
  assert.equal(findActiveRide([unknown], "driver"), null);
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
