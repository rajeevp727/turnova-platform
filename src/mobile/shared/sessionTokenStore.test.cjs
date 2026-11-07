const assert = require("node:assert/strict");
const test = require("node:test");
const {
  ACCESS_TOKEN_KEY,
  REFRESH_TOKEN_KEY,
  readSessionTokens,
  saveSessionTokens,
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

test("refreshes and persists a rotated pair when the restored access token is expired", async () => {
  const storage = createStorage({
    [ACCESS_TOKEN_KEY]: "expired-access",
    [REFRESH_TOKEN_KEY]: "refresh-1"
  });
  const calls = [];
  const restored = await restoreStoredSession(storage, async (path, init, token) => {
    calls.push({ path, init, token });
    if (path === "/api/v1/auth/me") throw httpError(401);
    return { accessToken: "access-2", refreshToken: "refresh-2", user: { userId: "driver-1" } };
  });

  assert.deepEqual(restored, {
    tokens: { accessToken: "access-2", refreshToken: "refresh-2" },
    user: { userId: "driver-1" }
  });
  assert.equal(storage.values.get(ACCESS_TOKEN_KEY), "access-2");
  assert.equal(storage.values.get(REFRESH_TOKEN_KEY), "refresh-2");
  assert.equal(calls.length, 2);
  assert.equal(calls[1].path, "/api/v1/auth/refresh");
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
