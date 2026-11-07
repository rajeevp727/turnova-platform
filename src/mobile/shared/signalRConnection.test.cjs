const assert = require("node:assert/strict");
const test = require("node:test");
const {
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
