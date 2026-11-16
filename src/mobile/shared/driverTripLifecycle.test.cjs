const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const {
  canLoadDriverRideHistory,
  canMaintainDriverRideSession
} = require("./driverTripLifecycle.js");

const tabs = ["Home", "Queue", "Trips", "Payslips", "Profile"];
const authenticatedDriver = {
  userId: "driver-1",
  hasAccessToken: true,
  isAuthenticated: true
};

test("authoritative Driver ride history loads independently of the selected tab", () => {
  for (const tab of tabs) {
    assert.equal(canLoadDriverRideHistory({ ...authenticatedDriver, tab }), true, tab);
  }
});

test("active Driver ride subscriptions and agreement history survive every tab selection", () => {
  for (const tab of tabs) {
    assert.equal(canMaintainDriverRideSession({
      ...authenticatedDriver,
      rideId: "ride-1",
      tab
    }), true, tab);
  }
});

test("Driver ride lifecycle fails closed without an authenticated Driver session", () => {
  for (const state of [
    { ...authenticatedDriver, userId: "" },
    { ...authenticatedDriver, userId: "   " },
    { ...authenticatedDriver, hasAccessToken: false },
    { ...authenticatedDriver, isAuthenticated: false },
    { userId: null, hasAccessToken: true, isAuthenticated: true }
  ]) {
    assert.equal(canLoadDriverRideHistory(state), false);
    assert.equal(canMaintainDriverRideSession({ ...state, rideId: "ride-1" }), false);
  }
});

test("ride-scoped subscriptions require a nonblank authoritative ride identifier", () => {
  for (const rideId of [undefined, null, "", "   "]) {
    assert.equal(canMaintainDriverRideSession({ ...authenticatedDriver, rideId }), false);
  }
  assert.equal(canMaintainDriverRideSession({ ...authenticatedDriver, rideId: "ride-1" }), true);
});

test("Driver App keeps history, agreement/status SignalR and trip location effects independent of Trips tab", () => {
  const appSource = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");

  assert.match(appSource, /canLoadDriverRideHistory\(/);
  assert.equal((appSource.match(/canMaintainDriverRideSession\(/g) || []).length, 4);
  assert.doesNotMatch(appSource, /tab !== "Trips"/);
  assert.match(appSource, /\}, \[user\?\.id, tokens\?\.accessToken, loadDriverRideHistory, activeRide\?\.status\]\);/);
  assert.match(appSource, /\}, \[user\?\.id, tokens\?\.accessToken, activeRide\?\.rideId, loadAgreementHistory\]\);/);
  assert.match(appSource, /\}, \[user\?\.id, activeRide\?\.rideId, tokens\?\.accessToken, loadAgreementHistory, apiFetch\]\);/);
  assert.match(appSource, /\}, \[user\?\.id, activeRide\?\.rideId, tokens\?\.accessToken, loadDriverRideHistory, apiFetch\]\);/);
  assert.match(appSource, /\}, \[user\?\.id, activeRide\?\.rideId, activeRide\?\.status, activeRide\?\.locationSharingActive, tokens\?\.accessToken, apiFetch\]\);/);
  assert.match(appSource, /Location sharing remains active while the app is foregrounded/);
});
