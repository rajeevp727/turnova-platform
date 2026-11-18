const assert = require("node:assert/strict");
const test = require("node:test");
const { readJsonResponseIfCurrent } = require("./sessionResponseGuard.js");

test("returns a parsed response body only while the captured session remains current", async () => {
  const body = { rideId: "ride-current", status: "Accepted" };
  assert.deepEqual(await readJsonResponseIfCurrent({ json: async () => body }, () => true), body);
});

test("rejects a response body that completes after logout advances the session epoch", async () => {
  let current = true;
  let releaseBody;
  const bodyPending = new Promise(resolve => { releaseBody = resolve; });
  const reading = readJsonResponseIfCurrent({ json: () => bodyPending }, () => current);
  await new Promise(resolve => setImmediate(resolve));
  current = false;
  releaseBody({ privateRide: "must-not-be-applied" });
  await assert.rejects(reading, /session changed/i);
});

test("uses an empty object for malformed JSON only when the session remains current", async () => {
  assert.deepEqual(await readJsonResponseIfCurrent({ json: async () => { throw new Error("invalid json"); } }, () => true), {});
  await assert.rejects(readJsonResponseIfCurrent({ json: async () => { throw new Error("invalid json"); } }, () => false), /session changed/i);
});
