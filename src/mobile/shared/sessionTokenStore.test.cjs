const assert = require("node:assert/strict");
const test = require("node:test");
const {
  ACCESS_TOKEN_KEY,
  REFRESH_TOKEN_KEY,
  readSessionTokens,
  readAccessTokenIfCurrent,
  saveSessionTokens,
  saveSessionTokensIfCurrent,
  clearSessionTokens,
  logoutSession,
  restoreStoredSession
} = require("./sessionTokenStore.js");

function createStorage(initial = {}, hooks = {}) {
  const values = new Map(Object.entries(initial));
  const calls = [];
  return {
    values,
    calls,
    async getItemAsync(key) {
      calls.push(["get", key]);
      if (hooks.get) await hooks.get(key);
      return values.has(key) ? values.get(key) : null;
    },
    async setItemAsync(key, value) {
      calls.push(["set", key, value]);
      if (hooks.set) await hooks.set(key, value);
      values.set(key, value);
    },
    async deleteItemAsync(key) {
      calls.push(["delete", key]);
      if (hooks.delete) await hooks.delete(key);
      values.delete(key);
    }
  };
}

function httpError(status) {
  const error = new Error(`HTTP ${status}`);
  error.status = status;
  return error;
}

test("saves a complete access and refresh token pair", async () => {
  const storage = createStorage();
  await saveSessionTokens(storage, { accessToken: "access-1", refreshToken: "refresh-1" });
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-1");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-1");
});

test("rolls back both token keys when the second secure write fails", async () => {
  const storage = createStorage({}, {
    set: async key => { if (key === REFRESH_TOKEN_KEY) throw new Error("secure write failed"); }
  });

  await assert.rejects(
    saveSessionTokens(storage, { accessToken: "access-1", refreshToken: "refresh-1" }),
    /Unable to securely save session tokens/
  );

  assert.equal(storage.values.has(ACCESS_TOKEN_KEY), false);
  assert.equal(storage.values.has(REFRESH_TOKEN_KEY), false);
  assert.deepEqual(storage.calls.filter(call => call[0] === "delete").map(call => call[1]), [
    ACCESS_TOKEN_KEY,
    REFRESH_TOKEN_KEY
  ]);
});

test("rejects incomplete token pairs before writing", async () => {
  const storage = createStorage();
  await assert.rejects(
    saveSessionTokens(storage, { accessToken: "access-1", refreshToken: "" }),
    /Invalid session tokens/
  );
  assert.equal(storage.calls.some(call => call[0] === "set"), false);
});

test("clears both keys even when deleting the access token fails", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "access-1",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  }, {
    delete: async key => { if (key === ACCESS_TOKEN_KEY) throw new Error("delete failed"); }
  });

  await assert.rejects(clearSessionTokens(storage), /Unable to clear stored session tokens/);
  assert.deepEqual(storage.calls.filter(call => call[0] === "delete").map(call => call[1]), [
    ACCESS_TOKEN_KEY,
    REFRESH_TOKEN_KEY
  ]);
  assert.equal(storage.values.has(REFRESH_TOKEN_KEY), false);
});

test("clears a partial token pair instead of restoring a half-session", async () => {
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "access-only" });
  assert.equal(await readSessionTokens(storage), null);
  assert.equal(storage.values.size, 0);
  assert.deepEqual(storage.calls.filter(call => call[0] === "delete").map(call => call[1]), [
    ACCESS_TOKEN_KEY,
    REFRESH_TOKEN_KEY
  ]);
});

test("restores a valid access token without refreshing", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "access-1",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });
  const calls = [];
  const restored = await restoreStoredSession(storage, async (path, init, token) => {
    calls.push({ path, init, token });
    return { userId: "rider-1" };
  });

  assert.deepEqual(restored, {
    tokens: { accessToken: "access-1", refreshToken: "refresh-1" },
    user: { userId: "rider-1" }
  });
  assert.equal(calls.length, 1);
  assert.equal(calls[0].path, "/api/v1/auth/me");
  assert.equal(calls[0].token, "access-1");
});

test("refreshes and reloads the profile with the rotated access token when the restored access token is expired", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "expired-access",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });
  const calls = [];
  const restored = await restoreStoredSession(storage, async (path, init, token) => {
    calls.push({ path, init, token });
    if (path === "/api/v1/auth/me" && token === "expired-access") throw httpError(401);
    if (path === "/api/v1/auth/refresh") {
      return { accessToken: "access-2", refreshToken: "refresh-2" };
    }
    assert.equal(path, "/api/v1/auth/me");
    assert.equal(token, "access-2");
    return { userId: "driver-1" };
  });

  assert.deepEqual(restored, {
    tokens: { accessToken: "access-2", refreshToken: "refresh-2" },
    user: { userId: "driver-1" }
  });
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-2");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-2");
  assert.equal(calls.length, 3);
  assert.equal(calls[1].path, "/api/v1/auth/refresh");
  assert.equal(calls[2].path, "/api/v1/auth/me");
  assert.equal(calls[2].token, "access-2");
});

test("preserves the rotated token pair when profile lookup fails temporarily after refresh", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "expired-access",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });
  const calls = [];

  await assert.rejects(
    restoreStoredSession(storage, async (path, init, token) => {
      calls.push({ path, token });
      if (path === "/api/v1/auth/me" && token === "expired-access") throw httpError(401);
      if (path === "/api/v1/auth/refresh") {
        return { accessToken: "access-2", refreshToken: "refresh-2" };
      }
      throw httpError(503);
    }),
    /HTTP 503/
  );

  assert.deepEqual(calls.map(call => [call.path, call.token]), [
    ["/api/v1/auth/me", "expired-access"],
    ["/api/v1/auth/refresh", undefined],
    ["/api/v1/auth/me", "access-2"]
  ]);
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-2");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-2");
});

test("clears the rotated token pair when the refreshed access token is rejected by profile lookup", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "expired-access",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });

  const restored = await restoreStoredSession(storage, async (path, init, token) => {
    if (path === "/api/v1/auth/me" && token === "expired-access") throw httpError(401);
    if (path === "/api/v1/auth/refresh") {
      return { accessToken: "access-2", refreshToken: "refresh-2" };
    }
    throw httpError(401);
  });

  assert.equal(restored, null);
  assert.equal(storage.values.size, 0);
});

test("clears both tokens when the access and refresh tokens are rejected", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "expired-access",
    [REFRESH_TOKEN_KEY]: "expired-refresh"
  });
  const restored = await restoreStoredSession(storage, async path => {
    throw httpError(401);
  });

  assert.equal(restored, null);
  assert.equal(storage.values.size, 0);
});

test("preserves tokens when app restart restoration fails because the network is unavailable", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "access-1",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });
  const networkError = new Error("network unavailable");

  await assert.rejects(
    restoreStoredSession(storage, async () => { throw networkError; }),
    /network unavailable/
  );

  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-1");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-1");
  assert.equal(storage.calls.some(call => call[0] === "delete"), false);
});

test("preserves tokens when the refresh endpoint is temporarily unavailable", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "expired-access",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });

  await assert.rejects(
    restoreStoredSession(storage, async path => {
      if (path === "/api/v1/auth/me") throw httpError(401);
      throw httpError(503);
    }),
    /HTTP 503/
  );

  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "expired-access");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-1");
});

test("surfaces secure-store read failures without creating an authenticated session", async () => {
  const storage = createStorage({}, {
    get: async key => { if (key === ACCESS_TOKEN_KEY) throw new Error("secure read failed"); }
  });

  await assert.rejects(readSessionTokens(storage), /secure read failed/);
});

test("clears local credentials when server logout is offline", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "access-1",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });

  await logoutSession(storage, async () => { throw new Error("network unavailable"); });

  assert.equal(storage.values.size, 0);
  assert.deepEqual(storage.calls.filter(call => call[0] === "delete").map(call => call[1]), [
    ACCESS_TOKEN_KEY,
    REFRESH_TOKEN_KEY
  ]);
});

test("reports local logout failure after attempting both secure deletions", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "access-1",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  }, {
    delete: async key => { if (key === REFRESH_TOKEN_KEY) throw new Error("secure delete failed"); }
  });

  await assert.rejects(
    logoutSession(storage, async () => { throw new Error("network unavailable"); }),
    /Unable to clear stored session tokens/
  );

  assert.equal(storage.calls.filter(call => call[0] === "delete").length, 2);
});

test("serializes logout clearing after a token-pair write already in progress", async () => {
  let signalAccessWriteStarted;
  let releaseAccessWrite;
  const accessWriteStarted = new Promise(resolve => { signalAccessWriteStarted = resolve; });
  const blockedAccessWrite = new Promise(resolve => { releaseAccessWrite = resolve; });
  const storage = createStorage({}, {
    set: async key => {
      if (key === ACCESS_TOKEN_KEY) {
        signalAccessWriteStarted();
        await blockedAccessWrite;
      }
    }
  });

  const saving = saveSessionTokens(storage, { accessToken: "access-1", refreshToken: "refresh-1" });
  await accessWriteStarted;
  const clearing = clearSessionTokens(storage);

  assert.deepEqual(storage.calls.map(call => call[0]), ["set"]);
  releaseAccessWrite();
  await Promise.all([saving, clearing]);

  assert.equal(storage.values.size, 0);
  assert.deepEqual(storage.calls.map(call => call[0]), ["set", "set", "delete", "delete"]);
});

test("does not wait for an unresponsive server logout before clearing local tokens", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "access-1",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });
  let resolveRevoke;
  const pendingRevoke = new Promise(resolve => { resolveRevoke = resolve; });

  await logoutSession(storage, () => pendingRevoke);

  assert.equal(storage.values.size, 0);
  resolveRevoke();
});


test("persists a token pair while the expected session epoch remains current", async () => {
  const storage = createStorage();
  const epoch = { current: 4 };

  const saved = await saveSessionTokensIfCurrent(
    storage,
    { accessToken: "access-current", refreshToken: "refresh-current" },
    () => epoch.current === 4
  );

  assert.equal(saved, true);
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-current");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-current");
});

test("skips token persistence when the session epoch is already stale", async () => {
  const storage = createStorage();
  const epoch = { current: 2 };

  const saved = await saveSessionTokensIfCurrent(
    storage,
    { accessToken: "access-stale", refreshToken: "refresh-stale" },
    () => epoch.current === 1
  );

  assert.equal(saved, false);
  assert.equal(storage.calls.some(call => call[0] === "set"), false);
  assert.equal(storage.values.size, 0);
});

test("rolls back an access-token write when logout advances the session epoch", async () => {
  let epoch = 1;
  let signalAccessWriteStarted;
  let releaseAccessWrite;
  const accessWriteStarted = new Promise(resolve => { signalAccessWriteStarted = resolve; });
  const blockedAccessWrite = new Promise(resolve => { releaseAccessWrite = resolve; });
  const storage = createStorage({}, {
    set: async key => {
      if (key === ACCESS_TOKEN_KEY) {
        signalAccessWriteStarted();
        await blockedAccessWrite;
      }
    }
  });

  const saving = saveSessionTokensIfCurrent(
    storage,
    { accessToken: "access-old", refreshToken: "refresh-old" },
    () => epoch === 1
  );
  await accessWriteStarted;

  epoch = 2;
  const logout = clearSessionTokens(storage);
  releaseAccessWrite();

  assert.equal(await saving, false);
  await logout;
  assert.equal(storage.values.size, 0);
});

test("rolls back a complete pair when logout advances the epoch during refresh-token persistence", async () => {
  let epoch = 7;
  let signalRefreshWriteStarted;
  let releaseRefreshWrite;
  const refreshWriteStarted = new Promise(resolve => { signalRefreshWriteStarted = resolve; });
  const blockedRefreshWrite = new Promise(resolve => { releaseRefreshWrite = resolve; });
  const storage = createStorage({}, {
    set: async key => {
      if (key === REFRESH_TOKEN_KEY) {
        signalRefreshWriteStarted();
        await blockedRefreshWrite;
      }
    }
  });

  const saving = saveSessionTokensIfCurrent(
    storage,
    { accessToken: "access-old", refreshToken: "refresh-old" },
    () => epoch === 7
  );
  await refreshWriteStarted;

  epoch = 8;
  const logout = clearSessionTokens(storage);
  releaseRefreshWrite();

  assert.equal(await saving, false);
  await logout;
  assert.equal(storage.values.size, 0);
});

test("a new login pair wins after an older refresh is invalidated during persistence", async () => {
  let epoch = 10;
  let signalAccessWriteStarted;
  let releaseAccessWrite;
  const accessWriteStarted = new Promise(resolve => { signalAccessWriteStarted = resolve; });
  const blockedAccessWrite = new Promise(resolve => { releaseAccessWrite = resolve; });
  const storage = createStorage({}, {
    set: async (key, value) => {
      if (key === ACCESS_TOKEN_KEY && value === "access-old") {
        signalAccessWriteStarted();
        await blockedAccessWrite;
      }
    }
  });

  const staleRefresh = saveSessionTokensIfCurrent(
    storage,
    { accessToken: "access-old", refreshToken: "refresh-old" },
    () => epoch === 10
  );
  await accessWriteStarted;

  epoch = 11;
  const clearPreviousSession = clearSessionTokens(storage);
  const newLogin = saveSessionTokensIfCurrent(
    storage,
    { accessToken: "access-new", refreshToken: "refresh-new" },
    () => epoch === 11
  );
  releaseAccessWrite();

  assert.equal(await staleRefresh, false);
  await clearPreviousSession;
  assert.equal(await newLogin, true);
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-new");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-new");
});


test("does not read or return a fallback access token after the session is already stale", async () => {
  let reads = 0;
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "stored-old" }, { get: async () => { reads += 1; } });
  const token = await readAccessTokenIfCurrent(storage, "memory-old", () => false);
  assert.equal(token, "");
  assert.equal(reads, 0);
});

test("does not return a stored or fallback access token when logout advances the epoch during secure read", async () => {
  let current = true;
  let signalReadStarted;
  let releaseRead;
  const readStarted = new Promise(resolve => { signalReadStarted = resolve; });
  const blockedRead = new Promise(resolve => { releaseRead = resolve; });
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "stored-old" }, { get: async () => { signalReadStarted(); await blockedRead; } });
  const reading = readAccessTokenIfCurrent(storage, "memory-old", () => current);
  await readStarted;
  current = false;
  releaseRead();
  assert.equal(await reading, "");
});

test("returns only the current session access token or its same-session fallback", async () => {
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "stored-current" });
  assert.equal(await readAccessTokenIfCurrent(storage, "memory-current", () => true), "stored-current");
  assert.equal(await readAccessTokenIfCurrent(createStorage(), "memory-current", () => true), "memory-current");
});


test("startup restore does not publish a profile after a replacement login advances the session epoch", async () => {
  let epoch = 3;
  let signalProfileStarted;
  let releaseProfile;
  const profileStarted = new Promise(resolve => { signalProfileStarted = resolve; });
  const pendingProfile = new Promise(resolve => { releaseProfile = resolve; });
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "access-old", [REFRESH_TOKEN_KEY]: "refresh-old" });
  const restoring = restoreStoredSession(storage, async path => {
    assert.equal(path, "/api/v1/auth/me");
    signalProfileStarted();
    await pendingProfile;
    return { userId: "old-account" };
  }, () => epoch === 3);
  await profileStarted;
  epoch = 4;
  assert.equal(await saveSessionTokensIfCurrent(storage, {
    accessToken: "access-new", refreshToken: "refresh-new"
  }, () => epoch === 4), true);
  releaseProfile();
  assert.equal(await restoring, null);
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-new");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-new");
});

test("startup restore does not persist a rotated pair when login supersedes an in-flight refresh", async () => {
  let epoch = 5;
  let signalRefreshStarted;
  let releaseRefresh;
  const refreshStarted = new Promise(resolve => { signalRefreshStarted = resolve; });
  const pendingRefresh = new Promise(resolve => { releaseRefresh = resolve; });
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "expired-old", [REFRESH_TOKEN_KEY]: "refresh-old" });
  const restoring = restoreStoredSession(storage, async path => {
    if (path === "/api/v1/auth/me") throw httpError(401);
    if (path === "/api/v1/auth/refresh") {
      signalRefreshStarted();
      await pendingRefresh;
      return { accessToken: "access-stale-refresh", refreshToken: "refresh-stale-refresh" };
    }
    throw new Error("Unexpected request.");
  }, () => epoch === 5);
  await refreshStarted;
  epoch = 6;
  assert.equal(await saveSessionTokensIfCurrent(storage, {
    accessToken: "access-new-login", refreshToken: "refresh-new-login"
  }, () => epoch === 6), true);
  releaseRefresh();
  assert.equal(await restoring, null);
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-new-login");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-new-login");
});

test("a stale startup restore rejection cannot clear a replacement login token pair", async () => {
  let epoch = 7;
  let signalRefreshStarted;
  let rejectRefresh;
  const refreshStarted = new Promise(resolve => { signalRefreshStarted = resolve; });
  const pendingRefresh = new Promise((resolve, reject) => { rejectRefresh = reject; });
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "expired-old", [REFRESH_TOKEN_KEY]: "refresh-old" });
  const restoring = restoreStoredSession(storage, async path => {
    if (path === "/api/v1/auth/me") throw httpError(401);
    if (path === "/api/v1/auth/refresh") {
      signalRefreshStarted();
      return pendingRefresh;
    }
    throw new Error("Unexpected request.");
  }, () => epoch === 7);
  await refreshStarted;
  epoch = 8;
  assert.equal(await saveSessionTokensIfCurrent(storage, {
    accessToken: "access-new-login", refreshToken: "refresh-new-login"
  }, () => epoch === 8), true);
  rejectRefresh(httpError(401));
  assert.equal(await restoring, null);
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-new-login");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-new-login");
});

test("partial-token cleanup rechecks the observed pair after a queued token-pair write", async () => {
  let signalRefreshWriteStarted;
  let releaseRefreshWrite;
  const refreshWriteStarted = new Promise(resolve => { signalRefreshWriteStarted = resolve; });
  const pendingRefreshWrite = new Promise(resolve => { releaseRefreshWrite = resolve; });
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "partial-old-access" }, {
    set: async key => {
      if (key === REFRESH_TOKEN_KEY) {
        signalRefreshWriteStarted();
        await pendingRefreshWrite;
      }
    }
  });

  const saving = saveSessionTokensIfCurrent(storage, {
    accessToken: "access-new-login", refreshToken: "refresh-new-login"
  }, () => true);
  await refreshWriteStarted;
  const reading = readSessionTokens(storage, () => true);
  releaseRefreshWrite();

  assert.equal(await saving, true);
  assert.equal(await reading, null);
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-new-login");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-new-login");
});


test("startup profile lookup after token rotation cannot publish after account switch", async () => {
  let epoch = 11;
  let profileCalls = 0;
  let signalFinalProfileStarted;
  let releaseFinalProfile;
  const finalProfileStarted = new Promise(resolve => { signalFinalProfileStarted = resolve; });
  const pendingFinalProfile = new Promise(resolve => { releaseFinalProfile = resolve; });
  const storage = createStorage({ [ACCESS_TOKEN_KEY]: "expired-old", [REFRESH_TOKEN_KEY]: "refresh-old" });

  const restoring = restoreStoredSession(storage, async (path, init, token) => {
    if (path === "/api/v1/auth/me" && profileCalls++ === 0) throw httpError(401);
    if (path === "/api/v1/auth/refresh") return { accessToken: "access-rotated-old", refreshToken: "refresh-rotated-old" };
    if (path === "/api/v1/auth/me" && token === "access-rotated-old") {
      signalFinalProfileStarted();
      await pendingFinalProfile;
      return { userId: "old-account" };
    }
    throw new Error("Unexpected request.");
  }, () => epoch === 11);

  await finalProfileStarted;
  epoch = 12;
  assert.equal(await saveSessionTokensIfCurrent(storage, {
    accessToken: "access-new-login", refreshToken: "refresh-new-login"
  }, () => epoch === 12), true);
  releaseFinalProfile();

  assert.equal(await restoring, null);
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-new-login");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-new-login");
});
