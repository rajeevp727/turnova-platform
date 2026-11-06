const assert = require("node:assert/strict");
const test = require("node:test");
const { canShareTripLocation, findAuthoritativeActiveTrip } = require("./tripLocationSharing.js");

const base = {
  appState: "active",
  mounted: true,
  status: "Accepted",
  locationSharingActive: true
};

test("trip location is shareable only while the assigned ride is active and app is foregrounded", () => {
  for (const status of ["Accepted", "Arrived", "InProgress"]) {
    assert.equal(canShareTripLocation({ ...base, status }), true);
  }
  assert.equal(canShareTripLocation({ ...base, appState: "background" }), false);
  assert.equal(canShareTripLocation({ ...base, appState: "inactive" }), false);
  assert.equal(canShareTripLocation({ ...base, mounted: false }), false);
  assert.equal(canShareTripLocation({ ...base, locationSharingActive: false }), false);
});

test("authoritative Driver history is required before sharing resumes", () => {
  const history = [
    { rideId: "completed", status: "Completed", locationSharingActive: false },
    { rideId: "stale-active", status: "Accepted", locationSharingActive: false },
    { rideId: "current", status: "InProgress", locationSharingActive: true }
  ];

  assert.equal(findAuthoritativeActiveTrip(history, "completed"), null);
  assert.equal(findAuthoritativeActiveTrip(history, "stale-active"), null);
  assert.equal(findAuthoritativeActiveTrip(history, "missing"), null);
  assert.equal(findAuthoritativeActiveTrip(history, "current")?.rideId, "current");
});

test("trip location is paused during negotiation and after terminal states", () => {
  for (const status of ["Negotiating", "AgreementConfirmed", "Completed", "Cancelled", "Expired"]) {
    assert.equal(canShareTripLocation({ ...base, status }), false);
  }
});
