const assert = require("node:assert/strict");
const test = require("node:test");
const { singleFlight, getFreshAccessToken, advanceSessionEpoch } = require("./refreshCoordinator.js");

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

test("shares the stored-token read and refresh for concurrent unauthorized requests", async () => {
  const ref = { current: null };
  const stored = { accessToken: "old-access", refreshToken: "old-refresh" };
  let reads = 0;
  let refreshCalls = 0;
  let resolveRefresh;
  const pending = new Promise(resolve => { resolveRefresh = resolve; });

  const readTokens = async () => {
    reads += 1;
    return { ...stored };
  };
  const refresh = async refreshToken => {
    refreshCalls += 1;
    assert.equal(refreshToken, "old-refresh");
    await pending;
    stored.accessToken = "new-access";
    stored.refreshToken = "new-refresh";
    return stored.accessToken;
  };

  const first = getFreshAccessToken(ref, "old-access", readTokens, refresh);
  const second = getFreshAccessToken(ref, "old-access", readTokens, refresh);

  assert.equal(first, second);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(reads, 1);
  assert.equal(refreshCalls, 1);
  resolveRefresh();
  assert.deepEqual(await Promise.all([first, second]), ["new-access", "new-access"]);
  assert.equal(ref.current, null);
});

test("a late 401 reuses the rotated access token instead of the stale refresh token", async () => {
  const ref = { current: null };
  const stored = { accessToken: "old-access", refreshToken: "old-refresh" };
  const usedRefreshTokens = [];
  const readTokens = async () => ({ ...stored });
  const refresh = async refreshToken => {
    usedRefreshTokens.push(refreshToken);
    stored.accessToken = "new-access";
    stored.refreshToken = "new-refresh";
    return stored.accessToken;
  };

  assert.equal(
    await getFreshAccessToken(ref, "old-access", readTokens, refresh),
    "new-access"
  );
  assert.equal(
    await getFreshAccessToken(ref, "old-access", readTokens, refresh),
    "new-access"
  );

  assert.deepEqual(usedRefreshTokens, ["old-refresh"]);
  assert.equal(stored.refreshToken, "new-refresh");
  assert.equal(ref.current, null);
});

test("missing refresh token fails without calling the refresh endpoint", async () => {
  const ref = { current: null };
  let refreshCalls = 0;

  await assert.rejects(
    getFreshAccessToken(
      ref,
      "old-access",
      async () => ({ accessToken: "old-access", refreshToken: null }),
      async () => { refreshCalls += 1; return "unexpected"; }
    ),
    /No refresh token available/
  );

  assert.equal(refreshCalls, 0);
  assert.equal(ref.current, null);
});


test("advancing the session epoch detaches an old refresh without clearing a newer in-flight refresh", async () => {
  const epoch = { current: 0 };
  const ref = { current: null };
  let resolveOldRefresh;
  let resolveNewRefresh;
  const oldResult = new Promise(resolve => { resolveOldRefresh = resolve; });
  const newResult = new Promise(resolve => { resolveNewRefresh = resolve; });

  const oldRefresh = singleFlight(ref, () => oldResult);
  await Promise.resolve();
  assert.equal(ref.current, oldRefresh);

  assert.equal(advanceSessionEpoch(epoch, ref), 1);
  assert.equal(ref.current, null);

  const newRefresh = singleFlight(ref, () => newResult);
  assert.notEqual(newRefresh, oldRefresh);
  await Promise.resolve();

  resolveOldRefresh("stale-access-token");
  assert.equal(await oldRefresh, "stale-access-token");
  assert.equal(ref.current, newRefresh);

  resolveNewRefresh("current-access-token");
  assert.equal(await newRefresh, "current-access-token");
  assert.equal(ref.current, null);
  assert.equal(epoch.current, 1);
});

test("rejects an invalid session epoch instead of silently resetting it", () => {
  const ref = { current: null };
  assert.throws(
    () => advanceSessionEpoch({ current: -1 }, ref),
    /Invalid session epoch/
  );
  assert.equal(ref.current, null);
});
