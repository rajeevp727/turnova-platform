const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const { hasActiveRiderRide } = require("./riderBookingGuard.js");

for (const status of [
  "Requested", "Negotiating", "AgreementConfirmed", "Accepted", "Arrived", "InProgress"
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
  assert.match(source, /loadRideHistory\(\(\)=>active&&isAuthenticatedSessionCurrent/);
  assert.match(source, /hasActiveSelectedRide/);
  assert.match(source, /item === "Book" && hasActiveSelectedRide/);
  const searchStart = source.indexOf("const findNearbyVehicles");
  const searchEnd = source.indexOf("const cancelRide", searchStart);
  assert.ok(searchStart >= 0 && searchEnd > searchStart);
  assert.match(source.slice(searchStart, searchEnd), /hasActiveRiderRide\(ride\)/);
  const bookingStart = source.indexOf("const requestSelectedDriver");
  const bookingEnd = source.indexOf("const loadAgreementHistory", bookingStart);
  assert.ok(bookingStart >= 0 && bookingEnd > bookingStart);
  assert.match(source.slice(bookingStart, bookingEnd), /hasActiveRiderRide\(ride\)/);
});
