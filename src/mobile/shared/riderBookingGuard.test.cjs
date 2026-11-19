const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const { canStartRiderBooking, hasActiveRiderRide, normalizeRiderSelectedBookingResponse } = require("./riderBookingGuard.js");

for (const status of [
  "Requested", "Matching", "Offered", "Negotiating", "AgreementConfirmed", "Accepted", "Arrived", "InProgress"
]) {
  test("active Rider booking guard blocks " + status, () => {
    assert.equal(hasActiveRiderRide({ status }), true);
  });
}

for (const status of ["Completed", "Cancelled", "Expired"]) {
  test("active Rider booking guard permits a new booking after " + status, () => {
    assert.equal(hasActiveRiderRide({ status }), false);
  });
}

test("active Rider booking guard distinguishes absent ride state from malformed ride records", () => {
  assert.equal(hasActiveRiderRide(null), false);
  assert.equal(hasActiveRiderRide(undefined), false);
  assert.equal(hasActiveRiderRide({}), true);
  assert.equal(hasActiveRiderRide({ status: null }), true);
  assert.equal(hasActiveRiderRide({ status: "" }), true);
  assert.equal(hasActiveRiderRide({ status: 42 }), true);
});

test("unknown future Rider lifecycle status blocks a new booking until known terminal confirmation", () => {
  const ride = { status: "FutureServerStatus" };
  assert.equal(hasActiveRiderRide(ride), true);
  assert.equal(canStartRiderBooking(true, ride), false);
});

test("Rider app loads the server-owned active ride before enabling booking", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /hasActiveRiderRide/);
  assert.match(source, /canStartRiderBooking\(activeRideStateLoaded,ride\)/);
  assert.match(source, /setActiveRideStateLoaded\(false\)/);
  assert.match(source, /setActiveRideStateLoaded\(true\)/);
  assert.match(source, /Checking active booking/);
  const resetStart = source.indexOf("const clearRiderSessionState");
  const resetEnd = source.indexOf("useEffect", resetStart);
  assert.ok(resetStart >= 0 && resetEnd > resetStart);
  assert.match(source.slice(resetStart, resetEnd), /setActiveRideStateLoaded\(false\)/);
  const historyStart = source.indexOf("const loadRideHistory");
  const historyEnd = source.indexOf("useEffect", historyStart);
  assert.ok(historyStart >= 0 && historyEnd > historyStart);
  const historySource = source.slice(historyStart, historyEnd);
  const initialRequestGuard = historySource.indexOf("if(!isActive()||!isAuthenticatedSessionCurrent(sessionEpoch,authenticatedSessionEpoch))return;");
  const requestSequenceStart = historySource.indexOf("const requestSequence=++rideHistoryRequestSequence.current");
  assert.ok(initialRequestGuard >= 0 && initialRequestGuard < requestSequenceStart);
  assert.match(historySource, /shouldApplyRideHistoryResponse\(requestSequence,rideHistoryRequestSequence\.current\)/);
  const loadingReset = historySource.indexOf("setActiveRideStateLoaded(false)");
  const historyRead = historySource.indexOf('apiFetch("/api/v1/rides/mine")');
  const detailRead = historySource.indexOf('apiFetch("/api/v1/rides/"+encodeURIComponent(active.rideId))');
  const historyShapeValidation = historySource.indexOf("isRiderRideHistoryShapeValid(historyResponse)");
  const detailShapeValidation = historySource.indexOf("isRiderRideDetailsResponseValid(detailsResponse,active.rideId)");
  const historyStored = historySource.lastIndexOf("setRideHistory(history)");
  const loaded = historySource.indexOf("setActiveRideStateLoaded(true)");
  assert.ok(loadingReset >= 0 && historyRead > loadingReset);
  assert.ok(historyShapeValidation > historyRead);
  assert.ok(detailRead > historyShapeValidation && detailShapeValidation > detailRead);
  assert.ok(historyStored > detailShapeValidation && loaded > historyStored);
  assert.match(historySource, /Ride history response is invalid/);
  assert.match(historySource, /Ride details response did not match the active history record/);
  assert.match(source, /loadRideHistory\(\(\)=>active&&isAuthenticatedSessionCurrent/);
  assert.match(source, /hasActiveSelectedRide/);
  assert.match(source, /item === "Book" && !canStartBooking/);
  const searchStart = source.indexOf("const findNearbyVehicles");
  const searchEnd = source.indexOf("const cancelRide", searchStart);
  assert.ok(searchStart >= 0 && searchEnd > searchStart);
  assert.match(source.slice(searchStart, searchEnd), /canStartRiderBooking\(activeRideStateLoaded,ride\)/);
  const bookingStart = source.indexOf("const requestSelectedDriver");
  const bookingEnd = source.indexOf("const loadAgreementHistory", bookingStart);
  assert.ok(bookingStart >= 0 && bookingEnd > bookingStart);
  assert.match(source.slice(bookingStart, bookingEnd), /canStartRiderBooking\(activeRideStateLoaded,ride\)/);
});

test("Rider active-ride hydration is triggered after sign-in and restored session", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");

  const hydrationStart = source.indexOf("useEffect(()=>{if(!user||!isAuthenticatedSessionCurrent(sessionEpoch,authenticatedSessionEpoch))return;");
  const hydrationEnd = source.indexOf("const resolvedTheme", hydrationStart);
  assert.ok(hydrationStart >= 0 && hydrationEnd > hydrationStart);
  const hydrationEffect = source.slice(hydrationStart, hydrationEnd);
  assert.match(hydrationEffect, /loadRideHistory\(\(\)=>active&&isAuthenticatedSessionCurrent\(sessionEpoch,authenticatedSessionEpoch\)\)/);
  assert.match(hydrationEffect, /\},\[user\?\.id,loadRideHistory,ride\?\.status\]\);/);

  const acceptStart = source.indexOf("const accept=useCallback");
  const acceptEnd = source.indexOf("const refresh=useCallback", acceptStart);
  assert.ok(acceptStart >= 0 && acceptEnd > acceptStart);
  const acceptSource = source.slice(acceptStart, acceptEnd);
  const acceptedEpoch = acceptSource.indexOf("authenticatedSessionEpoch.current=expectedEpoch;");
  const acceptedUser = acceptSource.indexOf("setUser(acceptedUser);");
  assert.ok(acceptedEpoch >= 0 && acceptedUser > acceptedEpoch);

  const restoreStart = source.indexOf("useEffect(()=>{let active=true;let restoreEpoch=sessionEpoch.current;");
  const restoreEnd = source.indexOf("useEffect(()=>{", restoreStart + 1);
  assert.ok(restoreStart >= 0 && restoreEnd > restoreStart);
  const restoreSource = source.slice(restoreStart, restoreEnd);
  const restoredEpoch = restoreSource.indexOf("authenticatedSessionEpoch.current=restoredEpoch;");
  const restoredUser = restoreSource.indexOf("setUser(restoredUser);");
  assert.ok(restoredEpoch >= 0 && restoredUser > restoredEpoch);
});

test("Rider booking guard fails closed until server-owned ride state is loaded", () => {
  assert.equal(canStartRiderBooking(false, null), false);
  assert.equal(canStartRiderBooking(false, { status: "Completed" }), false);
});

test("Rider booking guard permits booking only after a successful empty ride-history read", () => {
  assert.equal(canStartRiderBooking(true, null), true);
  assert.equal(canStartRiderBooking(true, { status: "Cancelled" }), true);
});

test("Rider booking guard blocks a hydrated active ride", () => {
  assert.equal(canStartRiderBooking(true, { status: "Negotiating" }), false);
  assert.equal(canStartRiderBooking(true, { status: "InProgress" }), false);
});


test("Rider session reset invalidates pending history and discovery responses", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const resetStart = source.indexOf("const clearRiderSessionState");
  const resetEnd = source.indexOf("useEffect", resetStart);
  assert.ok(resetStart >= 0 && resetEnd > resetStart);
  const resetSource = source.slice(resetStart, resetEnd);
  assert.ok(resetSource.includes("rideHistoryRequestSequence.current += 1;"));
  assert.ok(resetSource.includes("riderDiscoveryRequestSequence.current += 1;"));
});


const validSelectedBookingExpected = {
  riderId: "rider-1",
  driverId: "driver-1",
  unionId: "union-1",
  pickup: "KPHB",
  dropoff: "HITEC City",
  stops: ["Madhapur"],
  passengers: 2,
  pickupLatitude: 17.493,
  pickupLongitude: 78.399
};

const validSelectedBookingResponse = {
  rideId: "RIDE-SEL-0123456789",
  riderId: "rider-1",
  driverId: "driver-1",
  unionId: "union-1",
  pickup: "KPHB",
  dropoff: "HITEC City",
  passengers: 2,
  serviceClass: "cab",
  status: "Negotiating",
  locationSharingActive: false,
  estimatedFareInr: null,
  stops: ["Madhapur"],
  requestedAt: "2026-11-19T00:00:00Z",
  acceptedAt: null,
  unionName: "Union One",
  driverDisplayName: "Driver One",
  driverVehicleMake: "Toyota",
  driverVehicleModel: "Etios",
  driverDistanceToPickupKm: 2.5,
  negotiationExpiresAtUtc: "2026-11-19T00:15:00Z",
  pickupLatitude: 17.493,
  pickupLongitude: 78.399,
  driverLatitude: null,
  driverLongitude: null,
  auditEvents: [{ actorId: "internal-actor" }],
  agreementVersions: [{ amount: 999999 }],
  paymentReconciliation: { amountReceived: 1 },
  dispatchZoneId: "internal-zone"
};

test("selected booking response matches the authenticated Rider, selected Driver, Union and exact trip", () => {
  const normalized = normalizeRiderSelectedBookingResponse(
    validSelectedBookingResponse,
    validSelectedBookingExpected
  );
  assert.ok(normalized);
  assert.equal(normalized.rideId, validSelectedBookingResponse.rideId);
  assert.equal(normalized.riderId, undefined);
  assert.equal(normalized.driverId, undefined);
  assert.equal(normalized.unionId, "union-1");
  assert.equal(normalized.pickup, "KPHB");
  assert.equal(normalized.dropoff, "HITEC City");
  assert.deepEqual(normalized.stops, ["Madhapur"]);
  assert.equal(normalized.requestedAt, validSelectedBookingResponse.requestedAt);
  assert.equal(normalized.estimatedFareInr, null);
  assert.equal(normalized.locationSharingActive, false);
  assert.equal("auditEvents" in normalized, false);
  assert.equal("agreementVersions" in normalized, false);
  assert.equal("paymentReconciliation" in normalized, false);
  assert.equal("dispatchZoneId" in normalized, false);
});

for (const [name, override] of [
  ["Rider identity", { riderId: "another-rider" }],
  ["Driver identity", { driverId: "another-driver" }],
  ["Union identity", { unionId: "another-union" }],
  ["pickup", { pickup: "Other pickup" }],
  ["destination", { dropoff: "Other destination" }],
  ["passenger count", { passengers: 3 }],
  ["service class", { serviceClass: "bike" }],
  ["stops", { stops: [] }],
  ["ride ID", { rideId: " " }],
  ["lifecycle control characters", { status: "Negotiating\n" }],
  ["request timestamp", { requestedAt: "not-a-date" }],
  ["impossible request calendar date", { requestedAt: "2026-02-30T00:00:00Z" }],
  ["pickup latitude", { pickupLatitude: 91 }],
  ["pickup latitude echo", { pickupLatitude: 17.494 }],
  ["pickup longitude echo", { pickupLongitude: 78.4 }]
]) {
  test("selected booking response rejects mismatched " + name, () => {
    assert.equal(
      normalizeRiderSelectedBookingResponse(
        { ...validSelectedBookingResponse, ...override },
        validSelectedBookingExpected
      ),
      null
    );
  });
}

test("selected booking response exposes Driver coordinates only while an active ride shares location", () => {
  const active = normalizeRiderSelectedBookingResponse({
    ...validSelectedBookingResponse,
    status: "InProgress",
    locationSharingActive: true,
    driverLatitude: 17.4,
    driverLongitude: 78.4,
    driverLocationCapturedAtUtc: "2026-11-19T00:01:00Z"
  }, validSelectedBookingExpected);
  assert.ok(active);
  assert.equal(active.locationSharingActive, true);
  assert.equal(active.driverLatitude, 17.4);
  assert.equal(active.driverLongitude, 78.4);

  const paused = normalizeRiderSelectedBookingResponse({
    ...validSelectedBookingResponse,
    status: "InProgress",
    locationSharingActive: false,
    driverLatitude: 17.4,
    driverLongitude: 78.4
  }, validSelectedBookingExpected);
  assert.ok(paused);
  assert.equal(paused.locationSharingActive, false);
  assert.equal(paused.driverLatitude, undefined);
  assert.equal(paused.driverLongitude, undefined);

  assert.equal(normalizeRiderSelectedBookingResponse({
    ...validSelectedBookingResponse,
    status: "Negotiating",
    locationSharingActive: true
  }, validSelectedBookingExpected), null);
});

test("selected booking response requires a valid exact echo of the captured foreground pickup coordinates", () => {
  assert.equal(
    normalizeRiderSelectedBookingResponse(
      validSelectedBookingResponse,
      { ...validSelectedBookingExpected, pickupLatitude: 91 }
    ),
    null
  );
  assert.equal(
    normalizeRiderSelectedBookingResponse(
      validSelectedBookingResponse,
      { ...validSelectedBookingExpected, pickupLongitude: 181 }
    ),
    null
  );
});

test("selected booking response rejects a non-null platform fare estimate", () => {
  assert.equal(
    normalizeRiderSelectedBookingResponse(
      { ...validSelectedBookingResponse, estimatedFareInr: 125 },
      validSelectedBookingExpected
    ),
    null
  );
});

test("unknown future booking status remains unresolved and blocks another booking", () => {
  const normalized = normalizeRiderSelectedBookingResponse(
    { ...validSelectedBookingResponse, status: "FutureServerStatus" },
    validSelectedBookingExpected
  );
  assert.ok(normalized);
  assert.equal(normalized.status, "FutureServerStatus");
  assert.equal(canStartRiderBooking(true, normalized), false);
});

test("Rider validates and projects the selected booking response before committing state and rehydrates after uncertainty", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const bookingStart = source.indexOf("const requestSelectedDriver");
  const bookingEnd = source.indexOf("const loadAgreementHistory", bookingStart);
  assert.ok(bookingStart >= 0 && bookingEnd > bookingStart);
  const bookingSource = source.slice(bookingStart, bookingEnd);
  const responseValidation = bookingSource.indexOf("normalizeRiderSelectedBookingResponse(createdResponse,");
  const rideCommit = bookingSource.indexOf("setRide(created)");
  assert.ok(responseValidation >= 0 && rideCommit > responseValidation);
  assert.match(bookingSource, /pickupLatitude:coordinates\.latitude,pickupLongitude:coordinates\.longitude/);
  const catchStart = bookingSource.indexOf("catch(e){", rideCommit);
  const finallyStart = bookingSource.indexOf("finally{", catchStart);
  assert.ok(catchStart >= 0 && finallyStart > catchStart);
  assert.match(bookingSource.slice(catchStart, finallyStart), /await loadRideHistory\(\(\)=>isCurrent\(\)\)/);
});

test("RIDER-BOOKING-RESPONSE-003: Negotiating and AgreementConfirmed responses require a server expiry after request creation", () => {
  for (const status of ["Negotiating", "AgreementConfirmed"]) {
    for (const expiry of [
      null,
      "not-a-date",
      "2026-02-30T00:15:00Z",
      "2026-11-19T00:00:00Z",
      "2026-11-18T23:59:59Z",
      "2026-11-19T00:15:00"
    ]) {
      assert.equal(normalizeRiderSelectedBookingResponse({
        ...validSelectedBookingResponse,
        status,
        negotiationExpiresAtUtc: expiry
      }, validSelectedBookingExpected), null, status + " / " + String(expiry));
    }
    const valid = normalizeRiderSelectedBookingResponse({
      ...validSelectedBookingResponse,
      status,
      negotiationExpiresAtUtc: "2026-11-19T00:15:00+00:00"
    }, validSelectedBookingExpected);
    assert.ok(valid);
    assert.equal(valid.negotiationExpiresAtUtc, "2026-11-19T00:15:00+00:00");
  }
});

test("Rider selected-booking recovery permits a cleared expiry after the server advances beyond negotiation", () => {
  const accepted = normalizeRiderSelectedBookingResponse({
    ...validSelectedBookingResponse,
    status: "Accepted",
    negotiationExpiresAtUtc: null
  }, validSelectedBookingExpected);
  assert.ok(accepted);
  assert.equal(accepted.negotiationExpiresAtUtc, null);
});

test("Rider selected-booking response requires timezone-qualified server timestamps", () => {
  assert.equal(normalizeRiderSelectedBookingResponse({
    ...validSelectedBookingResponse,
    requestedAt: "2026-11-19T00:00:00"
  }, validSelectedBookingExpected), null);
});


test("RIDER-BOOKING-RESPONSE-004: rejects impossible calendar dates, invalid clock values and invalid offsets", () => {
  for (const requestedAt of [
    "2026-11-31T00:00:00Z",
    "2026-02-30T00:00:00Z",
    "2026-11-19T25:00:00Z",
    "2026-11-19T00:00:00+24:00",
    "2026-11-19T00:00:00+05:60"
  ]) {
    assert.equal(normalizeRiderSelectedBookingResponse({
      ...validSelectedBookingResponse,
      requestedAt
    }, validSelectedBookingExpected), null, requestedAt);
  }

  for (const negotiationExpiresAtUtc of [
    "2026-11-31T00:15:00Z",
    "2026-11-19T25:15:00Z",
    "2026-11-19T00:15:00+24:00",
    "2026-11-19T00:15:00+05:60"
  ]) {
    assert.equal(normalizeRiderSelectedBookingResponse({
      ...validSelectedBookingResponse,
      status: "Negotiating",
      negotiationExpiresAtUtc
    }, validSelectedBookingExpected), null, negotiationExpiresAtUtc);
  }

  const leapDay = normalizeRiderSelectedBookingResponse({
    ...validSelectedBookingResponse,
    requestedAt: "2024-02-29T00:00:00Z",
    status: "Negotiating",
    negotiationExpiresAtUtc: "2024-02-29T00:15:00Z"
  }, validSelectedBookingExpected);
  assert.ok(leapDay);
});
