const assert = require("node:assert/strict");
const test = require("node:test");
const {
  getJwtExpiryMilliseconds,
  getSignalRAccessToken,
  getSignalRReconnectDelayMs,
  startSignalRConnection
} = require("./signalRConnection.js");

test("SignalR reconnect delay grows exponentially and remains capped", () => {
  assert.deepEqual(
    Array.from({ length: 8 }, (_, retryCount) => getSignalRReconnectDelayMs(retryCount)),
    [1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000]
  );
});

test("SignalR reconnect delay rejects invalid retry counts", () => {
  for (const retryCount of [-1, 1.5, Number.NaN, Number.POSITIVE_INFINITY]) {
    assert.throws(() => getSignalRReconnectDelayMs(retryCount), RangeError);
  }
});

test("initial SignalR start retries until connected and stops when the owner is inactive", async () => {
  let attempts = 0;
  const delays = [];
  let active = true;
  const connected = await startSignalRConnection(
    {
      start: async () => {
        attempts += 1;
        if (attempts < 4) throw new Error("network unavailable");
      }
    },
    () => active,
    () => {},
    async milliseconds => { delays.push(milliseconds); }
  );

  assert.equal(connected, true);
  assert.equal(attempts, 4);
  assert.deepEqual(delays, [1000, 2000, 4000]);

  attempts = 0;
  const cancelled = await startSignalRConnection(
    {
      start: async () => {
        attempts += 1;
        throw new Error("network unavailable");
      }
    },
    () => active,
    () => {},
    async () => { active = false; }
  );
  assert.equal(cancelled, false);
  assert.equal(attempts, 1);
});


test("SignalR start that completes after session invalidation is stopped and reported inactive", async () => {
  let active = true;
  let finishStart;
  let stopCalls = 0;
  const startPending = new Promise(resolve => { finishStart = resolve; });
  const connection = {
    start: () => startPending,
    stop: async () => { stopCalls += 1; }
  };
  const starting = startSignalRConnection(connection, () => active);
  await new Promise(resolve => setImmediate(resolve));
  active = false;
  finishStart();
  assert.equal(await starting, false);
  assert.equal(stopCalls, 1);
});


function createJwt(payload) {
  const encodedPayload = Buffer.from(JSON.stringify(payload)).toString("base64url");
  return `eyJhbGciOiJub25lIn0.${encodedPayload}.signature`;
}

test("JWT expiry parser returns a finite expiry and fails closed for unknown tokens", () => {
  assert.equal(getJwtExpiryMilliseconds(createJwt({ exp: 1800 })), 1800000);
  assert.equal(getJwtExpiryMilliseconds(createJwt({ sub: "rider-1" })), null);
  assert.equal(getJwtExpiryMilliseconds("not-a-jwt"), null);
  assert.equal(getJwtExpiryMilliseconds(createJwt({ exp: "1800" })), null);
  assert.equal(getJwtExpiryMilliseconds(createJwt({ exp: Number.MAX_VALUE })), null);
});

test("SignalR uses an unexpired JWT without an unnecessary REST validation", async () => {
  const token = createJwt({ exp: 2000 });
  let validations = 0;
  let reads = 0;
  const result = await getSignalRAccessToken({
    readAccessToken: async () => { reads += 1; return token; },
    isCurrent: () => true,
    validateSession: async () => { validations += 1; },
    now: () => 1000000
  });

  assert.equal(result, token);
  assert.equal(reads, 1);
  assert.equal(validations, 0);
});

test("SignalR validates an expired JWT through /me and re-reads the rotated token", async () => {
  const expired = createJwt({ exp: 900 });
  const rotated = createJwt({ exp: 2400 });
  let reads = 0;
  let validations = 0;
  const result = await getSignalRAccessToken({
    readAccessToken: async () => (++reads === 1 ? expired : rotated),
    isCurrent: () => true,
    validateSession: async () => { validations += 1; },
    now: () => 1000000
  });

  assert.equal(result, rotated);
  assert.equal(reads, 2);
  assert.equal(validations, 1);
});

test("SignalR validates a JWT with missing expiry and returns only the server-validated token", async () => {
  const token = createJwt({ sub: "driver-1" });
  let reads = 0;
  let validations = 0;
  const result = await getSignalRAccessToken({
    readAccessToken: async () => { reads += 1; return token; },
    isCurrent: () => true,
    validateSession: async () => { validations += 1; },
    now: () => 1000000
  });

  assert.equal(result, token);
  assert.equal(reads, 2);
  assert.equal(validations, 1);
});

test("SignalR returns no token when the session is invalidated during /me validation", async () => {
  const expired = createJwt({ exp: 900 });
  let current = true;
  let reads = 0;
  const result = await getSignalRAccessToken({
    readAccessToken: async () => { reads += 1; return expired; },
    isCurrent: () => current,
    validateSession: async () => { current = false; },
    now: () => 1000000
  });

  assert.equal(result, "");
  assert.equal(reads, 1);
});

test("SignalR returns no token when the session changes during secure token read", async () => {
  let current = true;
  let validations = 0;
  const result = await getSignalRAccessToken({
    readAccessToken: async () => { current = false; return createJwt({ exp: 2400 }); },
    isCurrent: () => current,
    validateSession: async () => { validations += 1; },
    now: () => 1000000
  });

  assert.equal(result, "");
  assert.equal(validations, 0);
});

test("SignalR does not validate or return an absent access token", async () => {
  let validations = 0;
  const result = await getSignalRAccessToken({
    readAccessToken: async () => "",
    isCurrent: () => true,
    validateSession: async () => { validations += 1; },
    now: () => 1000000
  });

  assert.equal(result, "");
  assert.equal(validations, 0);
});
