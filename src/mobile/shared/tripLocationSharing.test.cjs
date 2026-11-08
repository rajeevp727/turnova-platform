const assert = require("node:assert/strict");
const test = require("node:test");
const {
  canShareTripLocation,
  findAuthoritativeActiveTrip,
  shouldApplyTripLocationUpdate,
  getRideMapFitCoordinates
} = require("./tripLocationSharing.js");

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

test("Rider accepts only a strictly newer capture timestamp for the same ride", () => {
  const current = {
    rideId: "ride-1",
    driverLatitude: 17.4,
    driverLongitude: 78.4,
    locationCapturedAtUtc: "2026-11-08T12:00:10.000Z",
    locationUpdatedAt: "2026-11-08T12:00:11.000Z"
  };
  const newer = {
    rideId: "ride-1",
    latitude: 17.401,
    longitude: 78.401,
    locationCapturedAtUtc: "2026-11-08T12:00:15.000Z",
    locationUpdatedAt: "2026-11-08T12:00:16.000Z"
  };

  assert.equal(shouldApplyTripLocationUpdate(current, newer), true);
  assert.equal(shouldApplyTripLocationUpdate(current, {
    ...newer,
    locationCapturedAtUtc: "2026-11-08T12:00:10.000Z"
  }), false);
  assert.equal(shouldApplyTripLocationUpdate(current, {
    ...newer,
    locationCapturedAtUtc: "2026-11-08T12:00:09.000Z"
  }), false);
});

test("Rider rejects mismatched ride, invalid coordinates and invalid timestamps", () => {
  const current = {
    rideId: "ride-2",
    locationCapturedAtUtc: "2026-11-08T12:00:10.000Z"
  };
  const update = {
    rideId: "ride-2",
    latitude: 17.4,
    longitude: 78.4,
    locationCapturedAtUtc: "2026-11-08T12:00:11.000Z"
  };

  assert.equal(shouldApplyTripLocationUpdate(current, { ...update, rideId: "ride-other" }), false);
  assert.equal(shouldApplyTripLocationUpdate(current, { ...update, latitude: 91 }), false);
  assert.equal(shouldApplyTripLocationUpdate(current, { ...update, longitude: 181 }), false);
  assert.equal(shouldApplyTripLocationUpdate(current, { ...update, locationCapturedAtUtc: "not-a-date" }), false);
  assert.equal(shouldApplyTripLocationUpdate(current, {
    ...update,
    locationCapturedAtUtc: "not-a-date",
    locationUpdatedAt: "2026-11-08T12:00:12.000Z"
  }), false);
});

test("first capture watermark upgrades a legacy receipt-only snapshot without mixing clock semantics", () => {
  const legacy = {
    rideId: "ride-legacy",
    locationUpdatedAt: "2026-11-08T12:00:30.000Z"
  };
  const captured = {
    rideId: "ride-legacy",
    latitude: 17.4,
    longitude: 78.4,
    locationCapturedAtUtc: "2026-11-08T12:00:20.000Z",
    locationUpdatedAt: "2026-11-08T12:00:31.000Z"
  };
  assert.equal(shouldApplyTripLocationUpdate(legacy, captured), true);
});

test("restored ride API capture watermark rejects an older SignalR capture", () => {
  const current = {
    rideId: "ride-restored",
    driverLocationCapturedAtUtc: "2026-11-08T12:00:30.000Z",
    locationUpdatedAt: "2026-11-08T12:00:31.000Z"
  };
  assert.equal(shouldApplyTripLocationUpdate(current, {
    rideId: "ride-restored",
    latitude: 17.4,
    longitude: 78.4,
    locationCapturedAtUtc: "2026-11-08T12:00:29.000Z",
    locationUpdatedAt: "2026-11-08T12:00:32.000Z"
  }), false);
  assert.equal(shouldApplyTripLocationUpdate(current, {
    rideId: "ride-restored",
    latitude: 17.4,
    longitude: 78.4,
    locationCapturedAtUtc: "2026-11-08T12:00:32.000Z",
    locationUpdatedAt: "2026-11-08T12:00:33.000Z"
  }), true);
});

test("legacy SignalR events without capture time remain ordered by server receipt time", () => {
  const current = { rideId: "ride-legacy", locationUpdatedAt: "2026-11-08T12:00:20.000Z" };
  assert.equal(shouldApplyTripLocationUpdate(current, {
    rideId: "ride-legacy", latitude: 17.4, longitude: 78.4,
    locationUpdatedAt: "2026-11-08T12:00:21.000Z"
  }), true);
  assert.equal(shouldApplyTripLocationUpdate(current, {
    rideId: "ride-legacy", latitude: 17.4, longitude: 78.4,
    locationUpdatedAt: "2026-11-08T12:00:19.000Z"
  }), false);

  const upgraded = { ...current, locationCapturedAtUtc: "2026-11-08T12:00:22.000Z" };
  assert.equal(shouldApplyTripLocationUpdate(upgraded, {
    rideId: "ride-legacy", latitude: 17.4, longitude: 78.4,
    locationUpdatedAt: "2026-11-08T12:00:23.000Z"
  }), false);
});

test("map viewport includes valid Driver and pickup coordinates only", () => {
  assert.deepEqual(getRideMapFitCoordinates({
    rideId: "ride-map",
    driverLatitude: 17.4,
    driverLongitude: 78.4,
    pickupLatitude: 17.5,
    pickupLongitude: 78.5
  }), [
    { latitude: 17.4, longitude: 78.4 },
    { latitude: 17.5, longitude: 78.5 }
  ]);
  assert.deepEqual(getRideMapFitCoordinates({
    rideId: "ride-map",
    driverLatitude: 91,
    driverLongitude: 78.4,
    pickupLatitude: null,
    pickupLongitude: null
  }), []);
});
