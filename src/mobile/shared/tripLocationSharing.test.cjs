const assert = require("node:assert/strict");
const test = require("node:test");
const { canShareTripLocation } = require("./tripLocationSharing.js");

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

test("trip location is paused during negotiation and after terminal states", () => {
  for (const status of ["Negotiating", "AgreementConfirmed", "Completed", "Cancelled", "Expired"]) {
    assert.equal(canShareTripLocation({ ...base, status }), false);
  }
});
