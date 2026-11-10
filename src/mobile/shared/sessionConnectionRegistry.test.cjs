const assert = require("node:assert/strict");
const test = require("node:test");
const { stopRegisteredConnections } = require("./sessionConnectionRegistry.js");

test("stops and clears every registered realtime connection exactly once", () => {
  const calls = [];
  const stops = new Set([
    () => { calls.push("rider"); },
    () => { calls.push("driver-offers"); },
    () => { calls.push("driver-ride"); }
  ]);
  stopRegisteredConnections(stops);
  stopRegisteredConnections(stops);
  assert.deepEqual(calls, ["rider", "driver-offers", "driver-ride"]);
  assert.equal(stops.size, 0);
});

test("contains synchronous and asynchronous transport-stop failures", async () => {
  const calls = [];
  const stops = new Set([
    () => { calls.push("sync"); throw new Error("transport stop failed"); },
    () => { calls.push("async"); return Promise.reject(new Error("async stop failed")); }
  ]);
  assert.doesNotThrow(() => stopRegisteredConnections(stops));
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(calls, ["sync", "async"]);
  assert.equal(stops.size, 0);
});
