const assert = require("node:assert/strict");
const test = require("node:test");
const {
  discoverNearbyCabDrivers,
  getForegroundPickupCoordinates
} = require("./foregroundPickupLocation.js");

function createLocation({ granted = true, latitude = 17.123, longitude = 78.456 } = {}) {
  const calls = { foregroundPermission: 0, position: 0, backgroundPermission: 0 };
  const location = {
    Accuracy: { Balanced: 3 },
    requestForegroundPermissionsAsync: async () => {
      calls.foregroundPermission += 1;
      return { granted };
    },
    requestBackgroundPermissionsAsync: async () => {
      calls.backgroundPermission += 1;
      return { granted: true };
    },
    getCurrentPositionAsync: async options => {
      calls.position += 1;
      assert.deepEqual(options, { accuracy: 3 });
      return { coords: { latitude, longitude } };
    }
  };
  return { location, calls };
}

test("Rider discovery does not read GPS or call the API when foreground permission is denied", async () => {
  const { location, calls } = createLocation({ granted: false });
  let discoveryRequests = 0;

  await assert.rejects(
    discoverNearbyCabDrivers(location, async () => {
      discoveryRequests += 1;
      return { unions: [] };
    }, 2),
    error => error.code === "LOCATION_PERMISSION_REQUIRED"
  );

  assert.equal(calls.foregroundPermission, 1);
  assert.equal(calls.position, 0);
  assert.equal(calls.backgroundPermission, 0);
  assert.equal(discoveryRequests, 0);
});

test("Rider discovery uses a fresh foreground GPS fix and sends the passenger count", async () => {
  const { location, calls } = createLocation();
  const requests = [];
  const expected = { journeyDistanceKm: null, unions: [] };

  const result = await discoverNearbyCabDrivers(location, async path => {
    requests.push(path);
    return expected;
  }, 3);

  assert.deepEqual(result.coordinates, { latitude: 17.123, longitude: 78.456 });
  assert.equal(result.discovery, expected);
  assert.deepEqual(requests, [
    "/api/v1/rider/discovery?latitude=17.123&longitude=78.456&passengers=3"
  ]);
  assert.equal(calls.foregroundPermission, 1);
  assert.equal(calls.position, 1);
  assert.equal(calls.backgroundPermission, 0);
});

test("invalid current GPS coordinates prevent a Rider discovery request", async t => {
  const invalidCoordinates = [
    { latitude: Number.NaN, longitude: 78.456 },
    { latitude: 91, longitude: 78.456 },
    { latitude: 17.123, longitude: 181 }
  ];

  for (const coordinates of invalidCoordinates) {
    await t.test(JSON.stringify(coordinates), async () => {
      const { location, calls } = createLocation(coordinates);
      let discoveryRequests = 0;

      await assert.rejects(
        discoverNearbyCabDrivers(location, async () => {
          discoveryRequests += 1;
          return { unions: [] };
        }, 1),
        error => error.code === "INVALID_PICKUP_COORDINATES"
      );

      assert.equal(calls.position, 1);
      assert.equal(discoveryRequests, 0);
    });
  }
});

test("selected-driver booking uses the same foreground-only coordinate validation", async () => {
  const { location, calls } = createLocation({ granted: false });

  await assert.rejects(
    getForegroundPickupCoordinates(location),
    error => error.code === "LOCATION_PERMISSION_REQUIRED"
  );

  assert.equal(calls.foregroundPermission, 1);
  assert.equal(calls.position, 0);
  assert.equal(calls.backgroundPermission, 0);
});
