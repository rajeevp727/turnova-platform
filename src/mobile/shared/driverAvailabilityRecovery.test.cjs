const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const {
  isDriverAvailabilityConflict,
  isDriverZoneExit,
  reconcileDriverAvailability,
  createDriverLocationRetryScheduler,
  isDriverTransientLocationError,
  DRIVER_LOCATION_RETRY_DELAY_MS
} = require("./driverAvailabilityRecovery.js");

function createState() {
  const state = { availability: null, history: null, activeRide: undefined };
  return {
    state,
    setAvailabilityStatus: value => { state.availability = value; },
    setDriverRideHistory: value => { state.history = value; },
    setActiveRide: value => { state.activeRide = value; }
  };
}

test("only HTTP 409 responses trigger server-state availability reconciliation", () => {
  assert.equal(isDriverAvailabilityConflict(Object.assign(new Error("Conflict"), { status: 409 })), true);
  assert.equal(isDriverAvailabilityConflict(Object.assign(new Error("Bad request"), { status: 400 })), false);
  assert.equal(isDriverAvailabilityConflict(Object.assign(new Error("Unavailable"), { status: 503 })), false);
  assert.equal(isDriverAvailabilityConflict(new Error("Network unavailable")), false);
  assert.equal(isDriverAvailabilityConflict(null), false);
});

test("only the exact authoritative HTTP 409 zone-exit response stops location watching", () => {
  const zoneExitMessage = "You left your assigned service zone and were taken offline.";
  assert.equal(isDriverZoneExit(Object.assign(new Error(zoneExitMessage), { status: 409 })), true);
  assert.equal(isDriverZoneExit(Object.assign(new Error(zoneExitMessage), { status: 503 })), false);
  assert.equal(isDriverZoneExit(Object.assign(new Error("Conflict"), { status: 409 })), false);
  assert.equal(isDriverZoneExit(Object.assign(new Error("You left your assigned service zone"), { status: 409 })), false);
  assert.equal(isDriverZoneExit(new Error(zoneExitMessage)), false);
  assert.equal(isDriverAvailabilityConflict(Object.assign(new Error(zoneExitMessage), { status: 409 })), true);
});

test("server Offline state replaces stale local Online state and stops retry eligibility", async () => {
  const { state, ...setters } = createState();
  const result = await reconcileDriverAvailability({
    fetchAvailability: async () => ({ availabilityStatus: "Offline" }),
    fetchRideHistory: async () => [],
    isActive: () => true,
    ...setters
  });

  assert.deepEqual(result, {
    applied: true,
    isOnline: false,
    activeRide: null,
    shouldRetryLocation: false
  });
  assert.equal(state.availability, "Offline");
  assert.deepEqual(state.history, []);
  assert.equal(state.activeRide, null);
});

test("selected negotiation recovered from server history pauses availability retries", async () => {
  const { state, ...setters } = createState();
  const activeRide = { rideId: "ride-1", status: "Negotiating" };
  const result = await reconcileDriverAvailability({
    fetchAvailability: async () => ({ availabilityStatus: "Online" }),
    fetchRideHistory: async () => [activeRide, { rideId: "old", status: "Completed" }],
    isActive: () => true,
    ...setters
  });

  assert.equal(result.isOnline, true);
  assert.equal(result.activeRide, activeRide);
  assert.equal(result.shouldRetryLocation, false);
  assert.equal(state.availability, "Online");
  assert.deepEqual(state.history, [activeRide, { rideId: "old", status: "Completed" }]);
  assert.equal(state.activeRide, activeRide);
});

test("online state with no active selected ride requests one delayed location retry", async () => {
  const { state, ...setters } = createState();
  const history = [{ rideId: "old", status: "Completed" }, { rideId: "offer", status: "Offered" }];
  const result = await reconcileDriverAvailability({
    fetchAvailability: async () => ({ availabilityStatus: "Online" }),
    fetchRideHistory: async () => history,
    isActive: () => true,
    ...setters
  });

  assert.equal(result.isOnline, true);
  assert.equal(result.activeRide, null);
  assert.equal(result.shouldRetryLocation, true);
  assert.equal(state.availability, "Online");
  assert.equal(state.activeRide, null);
});

test("stale effect results do not mutate availability or ride state", async () => {
  const { state, ...setters } = createState();
  const result = await reconcileDriverAvailability({
    fetchAvailability: async () => ({ availabilityStatus: "Offline" }),
    fetchRideHistory: async () => [{ rideId: "ride-1", status: "Negotiating" }],
    isActive: () => false,
    ...setters
  });

  assert.equal(result.applied, false);
  assert.equal(result.shouldRetryLocation, false);
  assert.equal(state.availability, null);
  assert.equal(state.history, null);
  assert.equal(state.activeRide, undefined);
});

test("failed reconciliation reads do not partially update local state", async () => {
  const { state, ...setters } = createState();
  await assert.rejects(reconcileDriverAvailability({
    fetchAvailability: async () => ({ availabilityStatus: "Offline" }),
    fetchRideHistory: async () => { throw new Error("history unavailable"); },
    isActive: () => true,
    ...setters
  }), /history unavailable/);

  assert.equal(state.availability, null);
  assert.equal(state.history, null);
  assert.equal(state.activeRide, undefined);
});

function createFakeTimers() {
  let nextId = 0;
  const timers = new Map();
  return {
    timers,
    setTimeout(callback, delayMs) {
      const id = ++nextId;
      timers.set(id, { callback, delayMs });
      return id;
    },
    clearTimeout(id) {
      timers.delete(id);
    },
    run(id) {
      const timer = timers.get(id);
      if (!timer) return false;
      timers.delete(id);
      timer.callback();
      return true;
    }
  };
}

test("transient location failures include network TypeError and retryable HTTP statuses only", () => {
  assert.equal(isDriverTransientLocationError(new TypeError("Network request failed")), true);
  for (const status of [408, 429, 500, 502, 503, 599]) {
    assert.equal(isDriverTransientLocationError(Object.assign(new Error("Temporary failure"), { status })), true);
  }
  for (const status of [400, 401, 403, 404, 409, 422, 499, 600]) {
    assert.equal(isDriverTransientLocationError(Object.assign(new Error("Permanent/conflict response"), { status })), false);
  }
  assert.equal(isDriverTransientLocationError(new Error("Unknown failure")), false);
  assert.equal(isDriverTransientLocationError(null), false);
});

test("foreground location retry schedules at most one 15-second timer and retries once", () => {
  const fake = createFakeTimers();
  let active = true;
  let retries = 0;
  const scheduler = createDriverLocationRetryScheduler({
    canRetry: () => active,
    retry: () => { retries += 1; },
    setTimeoutFn: fake.setTimeout,
    clearTimeoutFn: fake.clearTimeout
  });

  assert.equal(scheduler.schedule(), true);
  assert.equal(scheduler.schedule(), false);
  assert.equal(scheduler.isPending(), true);
  assert.equal(fake.timers.size, 1);
  const [id, timer] = fake.timers.entries().next().value;
  assert.equal(timer.delayMs, DRIVER_LOCATION_RETRY_DELAY_MS);
  assert.equal(timer.delayMs, 15000);
  assert.equal(fake.run(id), true);
  assert.equal(retries, 1);
  assert.equal(scheduler.isPending(), false);
});

test("retry callback is fenced when the app backgrounds before the timer fires", () => {
  const fake = createFakeTimers();
  let active = true;
  let retries = 0;
  const scheduler = createDriverLocationRetryScheduler({
    canRetry: () => active,
    retry: () => { retries += 1; },
    setTimeoutFn: fake.setTimeout,
    clearTimeoutFn: fake.clearTimeout
  });

  assert.equal(scheduler.schedule(), true);
  const id = fake.timers.keys().next().value;
  active = false;
  assert.equal(fake.run(id), true);
  assert.equal(retries, 0);
  assert.equal(scheduler.isPending(), false);
});

test("successful location publication can cancel a pending retry", () => {
  const fake = createFakeTimers();
  let retries = 0;
  const scheduler = createDriverLocationRetryScheduler({
    canRetry: () => true,
    retry: () => { retries += 1; },
    setTimeoutFn: fake.setTimeout,
    clearTimeoutFn: fake.clearTimeout
  });

  assert.equal(scheduler.schedule(), true);
  const id = fake.timers.keys().next().value;
  assert.equal(scheduler.cancel(), true);
  assert.equal(scheduler.cancel(), false);
  assert.equal(fake.run(id), false);
  assert.equal(retries, 0);
  assert.equal(scheduler.isPending(), false);
});

test("backgrounding and unmount cleanup cancel pending retry timers", () => {
  for (const lifecycle of ["background", "unmount"]) {
    const fake = createFakeTimers();
    let retries = 0;
    const scheduler = createDriverLocationRetryScheduler({
      canRetry: () => true,
      retry: () => { retries += 1; },
      setTimeoutFn: fake.setTimeout,
      clearTimeoutFn: fake.clearTimeout
    });

    assert.equal(scheduler.schedule(), true, lifecycle);
    const id = fake.timers.keys().next().value;
    assert.equal(scheduler.cancel(), true, lifecycle);
    assert.equal(fake.run(id), false, lifecycle);
    assert.equal(retries, 0, lifecycle);
    assert.equal(scheduler.isPending(), false, lifecycle);
  }
});

test("Driver App wires transient-only retry and cancels it on success, background and unmount", () => {
  const appSource = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");
  assert.match(appSource, /createDriverLocationRetryScheduler/);
  assert.match(appSource, /isDriverTransientLocationError\(e\)\) retryScheduler\.schedule\(\)/);
  assert.match(appSource, /if \(state\.shouldRetryLocation\) retryScheduler\.schedule\(\)/);
  assert.match(appSource, /await apiFetch\("\/api\/v1\/driver\/location"[\s\S]*?retryScheduler\.cancel\(\);/);
  assert.match(appSource, /const stopWatching = \(\) => \{[\s\S]*?retryScheduler\.cancel\(\);/);
});
