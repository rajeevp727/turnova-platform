const assert = require("node:assert/strict");
const test = require("node:test");
const { createSelectedBookingLocation } = require("./selectedBookingLocation.js");

function createLocation({ granted = true, latitude = 17.123, longitude = 78.456 } = {}) {
  const calls = { foregroundPermission: 0, position: 0, backgroundPermission: 0 };
  let currentCoordinates = { latitude, longitude };
  let permissionGranted = granted;
  const location = {
    Accuracy: { Balanced: 3 },
    requestForegroundPermissionsAsync: async () => {
      calls.foregroundPermission += 1;
      return { granted: permissionGranted };
    },
    requestBackgroundPermissionsAsync: async () => {
      calls.backgroundPermission += 1;
      return { granted: true };
    },
    getCurrentPositionAsync: async options => {
      calls.position += 1;
      assert.deepEqual(options, { accuracy: 3 });
      return { coords: currentCoordinates };
    }
  };

  return {
    location,
    calls,
    setCoordinates: coordinates => { currentCoordinates = coordinates; },
    setGranted: grantedValue => { permissionGranted = grantedValue; }
  };
}

test("selected booking captures a fresh foreground fix and reuses it for idempotent retries", async () => {
  const { location, calls, setCoordinates } = createLocation();
  const bookingLocation = createSelectedBookingLocation(location);

  const firstAttempt = await bookingLocation.capture();
  assert.deepEqual(firstAttempt, { latitude: 17.123, longitude: 78.456 });

  setCoordinates({ latitude: 17.456, longitude: 78.789 });
  const retry = await bookingLocation.capture();

  assert.deepEqual(retry, firstAttempt);
  assert.equal(calls.foregroundPermission, 1);
  assert.equal(calls.position, 1);
  assert.equal(calls.backgroundPermission, 0);

  bookingLocation.reset();
  const nextSelection = await bookingLocation.capture();

  assert.deepEqual(nextSelection, { latitude: 17.456, longitude: 78.789 });
  assert.equal(calls.foregroundPermission, 2);
  assert.equal(calls.position, 2);
});

test("concurrent selected-booking attempts share one foreground GPS read", async () => {
  const calls = { foregroundPermission: 0, position: 0 };
  let resolvePosition;
  const location = {
    Accuracy: { Balanced: 3 },
    requestForegroundPermissionsAsync: async () => {
      calls.foregroundPermission += 1;
      return { granted: true };
    },
    getCurrentPositionAsync: async options => {
      assert.deepEqual(options, { accuracy: 3 });
      calls.position += 1;
      return new Promise(resolve => { resolvePosition = resolve; });
    }
  };
  const bookingLocation = createSelectedBookingLocation(location);
  const first = bookingLocation.capture();
  const second = bookingLocation.capture();

  await new Promise(resolve => setImmediate(resolve));
  assert.equal(calls.position, 1);
  resolvePosition({ coords: { latitude: 17.123, longitude: 78.456 } });

  const [firstCoordinates, secondCoordinates] = await Promise.all([first, second]);
  assert.deepEqual(firstCoordinates, { latitude: 17.123, longitude: 78.456 });
  assert.deepEqual(secondCoordinates, firstCoordinates);
  assert.equal(calls.foregroundPermission, 1);
});

test("denied foreground permission is not cached and a later retry obtains a fresh fix", async () => {
  const { location, calls, setCoordinates, setGranted } = createLocation({ granted: false });
  const bookingLocation = createSelectedBookingLocation(location);

  await assert.rejects(
    bookingLocation.capture(),
    error => error.code === "LOCATION_PERMISSION_REQUIRED"
  );
  assert.equal(calls.position, 0);

  setGranted(true);
  setCoordinates({ latitude: 17.5, longitude: 78.9 });
  assert.deepEqual(await bookingLocation.capture(), { latitude: 17.5, longitude: 78.9 });
  assert.equal(calls.foregroundPermission, 2);
  assert.equal(calls.position, 1);
});

test("reset fences an in-flight GPS result from a changed booking attempt", async () => {
  const calls = { position: 0 };
  let resolveFirstPosition;
  const location = {
    Accuracy: { Balanced: 3 },
    requestForegroundPermissionsAsync: async () => ({ granted: true }),
    getCurrentPositionAsync: async () => {
      calls.position += 1;
      if (calls.position === 1) {
        return new Promise(resolve => { resolveFirstPosition = resolve; });
      }
      return { coords: { latitude: 17.9, longitude: 78.1 } };
    }
  };
  const bookingLocation = createSelectedBookingLocation(location);
  const staleAttempt = bookingLocation.capture();

  await new Promise(resolve => setImmediate(resolve));
  assert.equal(calls.position, 1);

  bookingLocation.reset();
  const currentAttempt = await bookingLocation.capture();
  assert.deepEqual(currentAttempt, { latitude: 17.9, longitude: 78.1 });

  resolveFirstPosition({ coords: { latitude: 17.1, longitude: 78.2 } });
  await assert.rejects(staleAttempt, /selected booking changed/i);
  assert.deepEqual(await bookingLocation.capture(), currentAttempt);
  assert.equal(calls.position, 2);
});
