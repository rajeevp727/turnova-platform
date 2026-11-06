const assert = require("node:assert/strict");
const test = require("node:test");
const { singleFlight } = require("./refreshCoordinator.js");

test("shares one refresh promise across concurrent unauthorized requests", async () => {
  const ref = { current: null };
  let refreshCalls = 0;
  let resolveRefresh;
  const refreshResult = new Promise(resolve => {
    resolveRefresh = resolve;
  });

  const first = singleFlight(ref, () => {
    refreshCalls += 1;
    return refreshResult;
  });
  const second = singleFlight(ref, () => {
    refreshCalls += 1;
    return Promise.resolve("unexpected-token");
  });

  assert.equal(first, second);
  await Promise.resolve();
  assert.equal(refreshCalls, 1);

  resolveRefresh("rotated-access-token");
  assert.deepEqual(await Promise.all([first, second]), [
    "rotated-access-token",
    "rotated-access-token"
  ]);
  assert.equal(ref.current, null);
});

test("clears a failed refresh so a later request can retry", async () => {
  const ref = { current: null };
  let refreshCalls = 0;

  await assert.rejects(
    singleFlight(ref, async () => {
      refreshCalls += 1;
      throw new Error("refresh rejected");
    }),
    /refresh rejected/
  );

  assert.equal(ref.current, null);
  const token = await singleFlight(ref, async () => {
    refreshCalls += 1;
    return "next-access-token";
  });

  assert.equal(token, "next-access-token");
  assert.equal(refreshCalls, 2);
  assert.equal(ref.current, null);
});
