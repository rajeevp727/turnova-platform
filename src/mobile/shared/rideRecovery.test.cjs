const assert = require("node:assert/strict");
const test = require("node:test");
const { applyRideStatusUpdate, findActiveRide, hasUnresolvedRiderRideHistory, isRiderRideDetailsResponseValid, isRiderRideHistoryShapeValid, shouldApplyRideHistoryResponse, shouldApplyRideStatusUpdate } = require("./rideRecovery.js");

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
    status: "InProgress",
    requestedAt: "2026-11-19T00:00:00Z",
    locationCapturedAtUtc: "2026-11-19T00:00:01Z"
  }, "ride-1"), true);
  for (const field of ["requestedAt", "acceptedAt", "negotiationExpiresAtUtc", "locationUpdatedAt", "locationCapturedAtUtc", "driverLocationCapturedAtUtc"]) {
    assert.equal(isRiderRideDetailsResponseValid({
      rideId: "ride-1",
      [field]: "2026-11-31T00:00:00Z"
    }, "ride-1"), false, field);
  }
  assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-1", status: "FutureServerStatus" }, "ride-1"), false);
  assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-1" }, "ride-1"), false);
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


test("RIDER-HISTORY-011: active ride details reject impossible optional server timestamps", () => {
  for (const field of ["requestedAt", "acceptedAt", "negotiationExpiresAtUtc", "locationUpdatedAt", "locationCapturedAtUtc", "driverLocationCapturedAtUtc"]) {
    assert.equal(isRiderRideDetailsResponseValid({
      rideId: "ride-1",
      [field]: "2026-11-31T00:00:00Z"
    }, "ride-1"), false, field);
  }
  assert.equal(isRiderRideDetailsResponseValid({
    rideId: "ride-1",
    requestedAt: "2024-02-29T00:00:00Z",
    locationCapturedAtUtc: null
  }, "ride-1"), true);
});


test("RIDER-HISTORY-012: unknown or malformed history remains unresolved alongside known active rows", () => {
  const active = { rideId: "active", status: "InProgress" };
  const unknown = { rideId: "future-status", status: "FutureServerStatus" };
  const malformed = { rideId: "missing-status" };

  assert.equal(hasUnresolvedRiderRideHistory([]), false);
  assert.equal(hasUnresolvedRiderRideHistory([{ rideId: "completed", status: "Completed" }]), false);
  assert.equal(hasUnresolvedRiderRideHistory([unknown]), true);
  assert.equal(hasUnresolvedRiderRideHistory([active, unknown]), true);
  assert.equal(hasUnresolvedRiderRideHistory([unknown, active]), true);
  assert.equal(hasUnresolvedRiderRideHistory([active, malformed]), true);
  assert.equal(hasUnresolvedRiderRideHistory([
    { rideId: "legacy-offer", status: "Offered" },
    { rideId: "selected-negotiation", status: "Negotiating" }
  ]), true);
  assert.equal(hasUnresolvedRiderRideHistory([
    { rideId: "active-one", status: "InProgress" },
    { rideId: "active-two", status: "Accepted" }
  ]), true);
  assert.equal(hasUnresolvedRiderRideHistory(null), true);
});

test("RIDER-HISTORY-012: Rider hydration checks all lifecycle values before detail lookup or success", () => {
  const source = require("node:fs").readFileSync(require("node:path").join(__dirname, "../rider-app/App.tsx"), "utf8");
  const start = source.indexOf("const loadRideHistory=useCallback");
  const end = source.indexOf("useEffect(()=>", start);
  assert.ok(start >= 0 && end > start);
  const hydration = source.slice(start, end);
  const shape = hydration.indexOf("isRiderRideHistoryShapeValid(historyResponse)");
  const unresolved = hydration.indexOf("hasUnresolvedRiderRideHistory(history)");
  const active = hydration.indexOf('findActiveRide(history, "rider")');
  const detail = hydration.indexOf('apiFetch("/api/v1/rides/"+encodeURIComponent(active.rideId))');
  const loaded = hydration.indexOf("setActiveRideStateLoaded(true)");
  assert.ok(shape >= 0 && unresolved > shape && active > unresolved);
  assert.ok(detail > active && loaded > detail);
  assert.match(hydration, /if\(hasUnresolvedRiderRideHistory\(history\)\)\{[\s\S]*?setRideHistory\(history\);[\s\S]*?Booking remains disabled/);
});

test("RIDER-HISTORY-013: active ride details require a known nonterminal lifecycle status", () => {
  const activeStatuses = [
    "Requested", "Matching", "Offered", "Negotiating",
    "AgreementConfirmed", "Accepted", "Arrived", "InProgress"
  ];
  for (const status of activeStatuses) {
    assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-1", status }, "ride-1"), true, status);
  }

  for (const status of ["Completed", "Cancelled", "Expired", "FutureServerStatus", "", " InProgress", "InProgress "]) {
    assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-1", status }, "ride-1"), false, status);
  }
  assert.equal(isRiderRideDetailsResponseValid({ rideId: "ride-1", status: null }, "ride-1"), false);
});

test("RIDER-HISTORY-013: Rider hydration validates detail lifecycle before committing active state", () => {
  const source = require("node:fs").readFileSync(require("node:path").join(__dirname, "../rider-app/App.tsx"), "utf8");
  const start = source.indexOf("const loadRideHistory=useCallback");
  const end = source.indexOf("useEffect(()=>", start);
  assert.ok(start >= 0 && end > start);
  const hydration = source.slice(start, end);
  const detailFetch = hydration.indexOf('apiFetch("/api/v1/rides/"+encodeURIComponent(active.rideId))');
  const validation = hydration.indexOf("isRiderRideDetailsResponseValid(detailsResponse,active.rideId)");
  const detailCommit = hydration.indexOf("activeDetails=detailsResponse as Ride");
  const bookingReady = hydration.indexOf("setActiveRideStateLoaded(true)");
  assert.ok(detailFetch >= 0 && validation > detailFetch);
  assert.ok(detailCommit > validation && bookingReady > detailCommit);
});
