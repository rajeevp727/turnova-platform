const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const { canStartRiderBooking, hasActiveRiderRide } = require("./riderBookingGuard.js");

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
  const historyStored = historySource.indexOf("setRideHistory(history)");
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
