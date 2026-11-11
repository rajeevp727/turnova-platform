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

for (const status of ["Completed", "Cancelled", "Expired", "Unknown"]) {
  test("active Rider booking guard permits a new booking after " + status, () => {
    assert.equal(hasActiveRiderRide({ status }), false);
  });
}

test("active Rider booking guard fails closed for missing ride state", () => {
  assert.equal(hasActiveRiderRide(null), false);
  assert.equal(hasActiveRiderRide(undefined), false);
  assert.equal(hasActiveRiderRide({}), false);
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
  const loadingReset = historySource.indexOf("setActiveRideStateLoaded(false)");
  const historyRead = historySource.indexOf('apiFetch("/api/v1/rides/mine")');
  const detailRead = historySource.indexOf('apiFetch("/api/v1/rides/"+encodeURIComponent(active.rideId))');
  const loaded = historySource.indexOf("setActiveRideStateLoaded(true)");
  assert.ok(loadingReset >= 0 && historyRead > loadingReset);
  assert.ok(detailRead >= 0 && loaded > detailRead);
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
