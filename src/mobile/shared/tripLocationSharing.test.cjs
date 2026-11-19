const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const {
  shouldMaintainDriverRideLifecycle,
  shouldWatchDriverTripLocation,
  canShareTripLocation,
  findAuthoritativeActiveTrip,
  shouldApplyTripLocationUpdate,
  mergeTripLocationSnapshot,
  applyRiderRideStatusUpdate,
  canShowRiderDriverLocation,
  getRideMapFitCoordinates,
  shouldFitRideMap
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

test("Rider driver map is visible only while an active ride shares valid coordinates", () => {
  const active = {
    rideId: "ride-visible",
    status: "InProgress",
    locationSharingActive: true,
    driverLatitude: 17.4,
    driverLongitude: 78.4
  };
  assert.equal(canShowRiderDriverLocation(active), true);
  assert.equal(canShowRiderDriverLocation({ ...active, status: "Completed" }), false);
  assert.equal(canShowRiderDriverLocation({ ...active, locationSharingActive: false }), false);
  assert.equal(canShowRiderDriverLocation({ ...active, driverLongitude: 181 }), false);
});

test("late Driver location events are rejected after sharing pauses or the ride becomes terminal", () => {
  const current = {
    rideId: "ride-terminal",
    status: "InProgress",
    locationSharingActive: true,
    driverLatitude: 17.4,
    driverLongitude: 78.4,
    locationCapturedAtUtc: "2026-11-08T12:00:10.000Z"
  };
  const update = {
    rideId: "ride-terminal",
    latitude: 17.5,
    longitude: 78.5,
    locationCapturedAtUtc: "2026-11-08T12:00:20.000Z",
    locationUpdatedAt: "2026-11-08T12:00:21.000Z"
  };
  assert.equal(shouldApplyTripLocationUpdate({ ...current, status: "Completed", locationSharingActive: false }, update), false);
  assert.equal(shouldApplyTripLocationUpdate({ ...current, locationSharingActive: false }, update), false);
});

test("terminal status update clears Driver GPS and cannot be regressed by a late active event", () => {
  const current = {
    rideId: "ride-terminal",
    status: "InProgress",
    locationSharingActive: true,
    driverLatitude: 17.4,
    driverLongitude: 78.4,
    driverLocationCapturedAtUtc: "2026-11-08T12:00:10.000Z",
    locationCapturedAtUtc: "2026-11-08T12:00:10.000Z",
    locationUpdatedAt: "2026-11-08T12:00:11.000Z"
  };
  const terminal = applyRiderRideStatusUpdate(current, { rideId: current.rideId, status: "Completed" });
  assert.equal(terminal.status, "Completed");
  assert.equal(terminal.locationSharingActive, false);
  assert.equal(terminal.driverLatitude, undefined);
  assert.equal(terminal.driverLongitude, undefined);
  assert.equal(terminal.driverLocationCapturedAtUtc, null);
  assert.equal(terminal.locationCapturedAtUtc, null);
  assert.equal(terminal.locationUpdatedAt, undefined);

  const regressed = applyRiderRideStatusUpdate(terminal, { rideId: current.rideId, status: "InProgress" });
  assert.equal(regressed.status, "Completed");
  assert.equal(regressed.driverLatitude, undefined);
});

test("history snapshot that disables sharing clears cached Driver coordinates", () => {
  const current = {
    rideId: "ride-snapshot",
    status: "InProgress",
    locationSharingActive: true,
    driverLatitude: 17.4,
    driverLongitude: 78.4,
    locationCapturedAtUtc: "2026-11-08T12:00:10.000Z",
    locationUpdatedAt: "2026-11-08T12:00:11.000Z"
  };
  const merged = mergeTripLocationSnapshot(current, {
    rideId: "ride-snapshot",
    status: "Completed",
    locationSharingActive: false,
    driverLatitude: 17.4,
    driverLongitude: 78.4,
    locationUpdatedAt: "2026-11-08T12:00:12.000Z"
  });
  assert.equal(merged.status, "Completed");
  assert.equal(merged.locationSharingActive, false);
  assert.equal(merged.driverLatitude, undefined);
  assert.equal(merged.driverLongitude, undefined);
  assert.equal(merged.locationUpdatedAt, undefined);

  const terminalCurrent = { ...current, status: "Completed", locationSharingActive: false, driverLatitude: undefined, driverLongitude: undefined };
  const staleActiveSnapshot = { ...current, status: "InProgress", locationSharingActive: true };
  const noResurrection = mergeTripLocationSnapshot(terminalCurrent, staleActiveSnapshot);
  assert.equal(noResurrection.status, "Completed");
  assert.equal(noResurrection.locationSharingActive, false);
  assert.equal(noResurrection.driverLatitude, undefined);
});

test("Rider accepts only a strictly newer capture timestamp for the same ride", () => {
  const current = {
    rideId: "ride-1",
    status: "Accepted",
    locationSharingActive: true,
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
    status: "Accepted",
    locationSharingActive: true,
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
    status: "Accepted",
    locationSharingActive: true,
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
    status: "InProgress",
    locationSharingActive: true,
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
  const current = { rideId: "ride-legacy", status: "Accepted", locationSharingActive: true, locationUpdatedAt: "2026-11-08T12:00:20.000Z" };
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

test("stale history refresh cannot overwrite a newer SignalR location snapshot", () => {
  const current = {
    rideId: "ride-refresh",
    status: "Accepted",
    locationSharingActive: true,
    driverLatitude: 17.4,
    driverLongitude: 78.4,
    locationCapturedAtUtc: "2026-11-08T12:00:30.000Z",
    locationUpdatedAt: "2026-11-08T12:00:31.000Z"
  };
  const staleSnapshot = {
    rideId: "ride-refresh",
    status: "InProgress",
    locationSharingActive: true,
    driverLatitude: 17.3,
    driverLongitude: 78.3,
    driverLocationCapturedAtUtc: "2026-11-08T12:00:29.000Z",
    locationUpdatedAt: "2026-11-08T12:00:32.000Z"
  };
  const mergedStale = mergeTripLocationSnapshot(current, staleSnapshot);
  assert.equal(mergedStale.status, "InProgress");
  assert.equal(mergedStale.driverLatitude, 17.4);
  assert.equal(mergedStale.driverLongitude, 78.4);
  assert.equal(mergedStale.locationCapturedAtUtc, "2026-11-08T12:00:30.000Z");

  const newerSnapshot = {
    ...staleSnapshot,
    driverLatitude: 17.5,
    driverLongitude: 78.5,
    driverLocationCapturedAtUtc: "2026-11-08T12:00:35.000Z",
    locationUpdatedAt: "2026-11-08T12:00:36.000Z"
  };
  const mergedNewer = mergeTripLocationSnapshot(current, newerSnapshot);
  assert.equal(mergedNewer.driverLatitude, 17.5);
  assert.equal(mergedNewer.driverLongitude, 78.5);
  assert.equal(mergedNewer.locationCapturedAtUtc, "2026-11-08T12:00:35.000Z");
});

test("Rider status events invalidate pending history and use the location-clearing transition", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const start = source.indexOf('connection.on("RideStatusChanged"');
  const end = source.indexOf('connection.on("RideAgreementChanged"', start);
  assert.ok(start >= 0 && end > start);
  const handler = source.slice(start, end);
  const invalidation = handler.indexOf("rideHistoryRequestSequence.current+=1");
  const statusUpdate = handler.indexOf("applyRiderRideStatusUpdate(current,update)");
  assert.ok(invalidation >= 0 && statusUpdate > invalidation);
  assert.ok(source.includes("canShowRiderDriverLocation(ride)"));
});

test("Rider camera follows only after the matching map is ready and follow is enabled", () => {
  const state = { tab: "Activity", rideId: "ride-map", readyRideId: null, followDriverLocation: true };
  assert.equal(shouldFitRideMap(state), false);
  assert.equal(shouldFitRideMap({ ...state, readyRideId: "ride-map" }), true);
  assert.equal(shouldFitRideMap({ ...state, readyRideId: "ride-map", followDriverLocation: false }), false);
  assert.equal(shouldFitRideMap({ ...state, readyRideId: "another-ride" }), false);
  assert.equal(shouldFitRideMap({ ...state, tab: "Book", readyRideId: "ride-map" }), false);
});

test("map viewport includes valid Driver and pickup coordinates only", () => {
  assert.deepEqual(getRideMapFitCoordinates({
    rideId: "ride-map",
    status: "Accepted",
    locationSharingActive: true,
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


test("session invalidation immediately makes an otherwise active foreground location watcher non-shareable", () => {
  let mounted = true;
  const isSessionCurrent = () => mounted;
  const canShare = () => isSessionCurrent() && canShareTripLocation({ ...base, mounted });
  assert.equal(canShare(), true);
  mounted = false;
  assert.equal(canShare(), false);
});


const driverRideContext = {
  userId: "driver-1",
  rideId: "ride-1",
  hasAccessToken: true,
  sessionCurrent: true,
  status: "Accepted",
  locationSharingActive: true
};

test("active Driver ride lifecycle is independent of the selected app tab", () => {
  for (const tab of ["Home", "Queue", "Trips", "Payslips", "Profile"]) {
    assert.equal(shouldMaintainDriverRideLifecycle({ ...driverRideContext, tab }), true, tab);
  }
  for (const status of ["Negotiating", "AgreementConfirmed", "Accepted", "Arrived", "InProgress"]) {
    assert.equal(shouldMaintainDriverRideLifecycle({ ...driverRideContext, status }), true, status);
  }
  for (const status of ["Completed", "Cancelled", "Expired", "Rejected", "Unknown"]) {
    assert.equal(shouldMaintainDriverRideLifecycle({ ...driverRideContext, status }), false, status);
  }
  for (const invalid of [
    { userId: "" },
    { rideId: "" },
    { hasAccessToken: false },
    { sessionCurrent: false }
  ]) {
    assert.equal(shouldMaintainDriverRideLifecycle({ ...driverRideContext, ...invalid }), false);
  }
});

test("foreground trip GPS requires an authenticated active ride and server-enabled location sharing", () => {
  for (const status of ["Accepted", "Arrived", "InProgress"]) {
    assert.equal(shouldWatchDriverTripLocation({ ...driverRideContext, status }), true, status);
  }
  for (const status of ["Negotiating", "AgreementConfirmed", "Completed", "Cancelled", "Expired"]) {
    assert.equal(shouldWatchDriverTripLocation({ ...driverRideContext, status }), false, status);
  }
  assert.equal(shouldWatchDriverTripLocation({ ...driverRideContext, locationSharingActive: false }), false);
  assert.equal(shouldWatchDriverTripLocation({ ...driverRideContext, sessionCurrent: false }), false);
});

function driverEffectContaining(source, marker) {
  const markerIndex = source.indexOf(marker);
  assert.notEqual(markerIndex, -1, "missing Driver App wiring marker: " + marker);
  const start = source.lastIndexOf("  useEffect(() => {", markerIndex);
  const end = source.indexOf("\n  }, [", markerIndex);
  const close = source.indexOf("]);", end);
  assert.ok(start >= 0 && end > markerIndex && close > end, "unable to isolate effect for: " + marker);
  return source.slice(start, close + 3);
}

test("Driver active ride history, agreement and SignalR effects do not depend on Trips navigation", () => {
  const source = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");
  assert.doesNotMatch(source, /tab\s*!==\s*["']Trips["']/);
  assert.match(source, /shouldMaintainDriverRideLifecycle/);
  assert.match(source, /shouldWatchDriverTripLocation/);

  for (const marker of [
    "loadDriverRideHistory(isHistoryCurrent)",
    "loadAgreementHistory(activeRide.rideId, () => active)",
    'connection.on("RideAgreementChanged", refreshAgreement);',
    'connection.on("RideStatusChanged", handleRideStatusChanged);',
    "findAuthoritativeActiveTrip(history, activeRide.rideId)"
  ]) {
    assert.doesNotMatch(driverEffectContaining(source, marker), /\btab\b/, marker);
  }
  assert.match(driverEffectContaining(source, "loadDriverRideHistory(isHistoryCurrent)"), /activeRide\?\.status/);
  assert.match(driverEffectContaining(source, 'connection.on("RideAgreementChanged", refreshAgreement);'), /activeRide\?\.status/);
  assert.match(driverEffectContaining(source, 'connection.on("RideStatusChanged", handleRideStatusChanged);'), /activeRide\?\.status/);
  assert.match(driverEffectContaining(source, "findAuthoritativeActiveTrip(history, activeRide.rideId)"), /activeRide\?\.status/);
  assert.match(source, /Foreground location sharing continues while your active trip remains eligible/);
});

test("Driver foreground resume rechecks authoritative history before recreating the GPS watcher", () => {
  const source = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");
  const effect = driverEffectContaining(source, "findAuthoritativeActiveTrip(history, activeRide.rideId)");
  const historyRead = effect.indexOf('apiFetch("/api/v1/driver/rides/mine")');
  const authoritativeCheck = effect.indexOf("findAuthoritativeActiveTrip(history, activeRide.rideId)");
  const watcherStart = effect.indexOf("Location.watchPositionAsync");
  assert.ok(historyRead >= 0 && authoritativeCheck > historyRead && watcherStart > authoritativeCheck);
  assert.match(effect, /if \(state === "active"\) void startWatching\(\)/);
});

test("Rider status update safely preserves a missing ride and narrows the React state updater", () => {
  assert.equal(applyRiderRideStatusUpdate(null, { rideId: "ride-missing", status: "Completed" }), null);
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /setRide\(current=>current\?applyRiderRideStatusUpdate\(current,update\):current\)/);
});

test("RIDER-LOCATION-UI-003: a conflicting terminal history snapshot cannot replace an established terminal outcome", () => {
  const current = {
    rideId: "ride-terminal-conflict",
    status: "Completed",
    locationSharingActive: false,
    driverLatitude: 17.4,
    driverLongitude: 78.4
  };
  const staleTerminal = {
    rideId: "ride-terminal-conflict",
    status: "Cancelled",
    locationSharingActive: false,
    driverLatitude: 17.5,
    driverLongitude: 78.5
  };
  const merged = mergeTripLocationSnapshot(current, staleTerminal);
  assert.equal(merged.status, "Completed");
  assert.equal(merged.locationSharingActive, false);
  assert.equal(merged.driverLatitude, undefined);
  assert.equal(merged.driverLongitude, undefined);
});


test("RIDER-LOCATION-UI-005: live driver map and markers expose accessible context", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const mapStart = source.indexOf("<MapView");
  const mapEnd = source.indexOf("</MapView>", mapStart);
  assert.ok(mapStart >= 0 && mapEnd > mapStart);
  const map = source.slice(mapStart, mapEnd);

  assert.match(map, /testID="rider-live-driver-map"/);
  assert.match(map, /accessible=\{true\}/);
  assert.match(map, /accessibilityLabel="Live driver location map"/);
  assert.match(map, /accessibilityHint="Shows your driver's latest shared location and your pickup point\. Moving the map pauses automatic following\."/);
  assert.match(map, /onPanDrag=\{\(\)=>setFollowDriverLocation\(false\)\}/);
  assert.match(map, /title="Your driver" description="Latest location shared by your selected driver during this active ride\."/);
  assert.match(map, /title="Pickup" description="Your selected pickup location\."/);
});

test("RIDER-LOCATION-UI-006: Rider status events rehydrate server-owned sharing state before location updates", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const statusStart = source.indexOf('connection.on("RideStatusChanged"');
  const statusEnd = source.indexOf('connection.on("RideAgreementChanged"', statusStart);
  assert.ok(statusStart >= 0 && statusEnd > statusStart);
  const handler = source.slice(statusStart, statusEnd);
  const stateUpdate = handler.indexOf("setRide(current=>current?applyRiderRideStatusUpdate(current,update):current)");
  const refresh = handler.indexOf("void refreshRiderState()");
  assert.ok(stateUpdate >= 0 && refresh > stateUpdate);

  const refreshStart = source.indexOf("const refreshRiderState=async()=>{");
  const refreshEnd = source.indexOf('connection.on("DriverLocationUpdated"', refreshStart);
  assert.ok(refreshStart >= 0 && refreshEnd > refreshStart);
  const refreshBody = source.slice(refreshStart, refreshEnd);
  assert.ok(refreshBody.indexOf("await loadRideHistory") >= 0);
  assert.ok(refreshBody.indexOf("await refreshAgreements") > refreshBody.indexOf("await loadRideHistory"));
});
