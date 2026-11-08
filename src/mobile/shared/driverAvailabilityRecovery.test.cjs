const assert = require("node:assert/strict");
const test = require("node:test");
const {
  isDriverAvailabilityConflict,
  reconcileDriverAvailability
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
