const ACCESS_TOKEN_KEY = "turnova.access";
const REFRESH_TOKEN_KEY = "turnova.refresh";
const mutationQueues = new WeakMap();

function serializeMutation(storage, operation) {
  const previous = mutationQueues.get(storage) ?? Promise.resolve();
  const current = previous.catch(() => undefined).then(operation);
  mutationQueues.set(storage, current.catch(() => undefined));
  return current;
}

async function clearSessionTokensCore(storage) {
  const results = await Promise.allSettled([
    Promise.resolve().then(() => storage.deleteItemAsync(ACCESS_TOKEN_KEY)),
    Promise.resolve().then(() => storage.deleteItemAsync(REFRESH_TOKEN_KEY))
  ]);
  if (results.some(result => result.status === "rejected")) {
    throw new Error("Unable to clear stored session tokens.");
  }
}

async function readAccessTokenIfCurrent(storage, fallbackToken, isCurrent) {
  if (typeof isCurrent !== "function") throw new Error("A session-current predicate is required.");
  if (!isCurrent()) return "";
  const storedToken = await storage.getItemAsync(ACCESS_TOKEN_KEY);
  if (!isCurrent()) return "";
  return storedToken ?? fallbackToken ?? "";
}

function clearSessionTokens(storage) {
  return serializeMutation(storage, () => clearSessionTokensCore(storage));
}

function validateSessionTokens(tokens) {
  if (
    !tokens ||
    typeof tokens.accessToken !== "string" ||
    tokens.accessToken.length === 0 ||
    typeof tokens.refreshToken !== "string" ||
    tokens.refreshToken.length === 0
  ) {
    throw new Error("Invalid session tokens.");
  }
}

async function saveSessionTokensCore(storage, tokens, isCurrent) {
  try {
    if (!isCurrent()) return false;

    await storage.setItemAsync(ACCESS_TOKEN_KEY, tokens.accessToken);
    if (!isCurrent()) {
      await clearSessionTokensCore(storage);
      return false;
    }

    await storage.setItemAsync(REFRESH_TOKEN_KEY, tokens.refreshToken);
    if (!isCurrent()) {
      await clearSessionTokensCore(storage);
      return false;
    }

    return true;
  } catch {
    try {
      await clearSessionTokensCore(storage);
    } catch {
      // Preserve the save failure; callers surface a recoverable storage error.
    }
    throw new Error("Unable to securely save session tokens.");
  }
}

async function saveSessionTokens(storage, tokens) {
  validateSessionTokens(tokens);
  return serializeMutation(storage, async () => {
    await saveSessionTokensCore(storage, tokens, () => true);
  });
}

function saveSessionTokensIfCurrent(storage, tokens, isCurrent) {
  validateSessionTokens(tokens);
  if (typeof isCurrent !== "function") {
    throw new Error("A session-current predicate is required.");
  }

  return serializeMutation(storage, () => saveSessionTokensCore(storage, tokens, isCurrent));
}

async function readSessionTokens(storage, isCurrent = () => true) {
  if (typeof isCurrent !== "function") throw new Error("A session-current predicate is required.");
  if (!isCurrent()) return null;
  const [accessToken, refreshToken] = await Promise.all([
    storage.getItemAsync(ACCESS_TOKEN_KEY),
    storage.getItemAsync(REFRESH_TOKEN_KEY)
  ]);
  if (!isCurrent()) return null;
  if (accessToken && refreshToken) return { accessToken, refreshToken };
  if (accessToken || refreshToken) {
    await serializeMutation(storage, async () => {
      if (!isCurrent()) return;
      const [currentAccessToken, currentRefreshToken] = await Promise.all([
        storage.getItemAsync(ACCESS_TOKEN_KEY),
        storage.getItemAsync(REFRESH_TOKEN_KEY)
      ]);
      if (!isCurrent()) return;
      if (currentAccessToken === accessToken && currentRefreshToken === refreshToken) {
        await clearSessionTokensCore(storage);
      }
    });
  }
  return null;
}

function clearSessionTokensIfCurrent(storage, isCurrent, expectedTokens) {
  if (typeof isCurrent !== "function") throw new Error("A session-current predicate is required.");
  return serializeMutation(storage, async () => {
    if (!isCurrent()) return false;
    const [accessToken, refreshToken] = await Promise.all([
      storage.getItemAsync(ACCESS_TOKEN_KEY),
      storage.getItemAsync(REFRESH_TOKEN_KEY)
    ]);
    if (!isCurrent()) return false;
    if (expectedTokens && (
      accessToken !== expectedTokens.accessToken ||
      refreshToken !== expectedTokens.refreshToken
    )) return false;
    if (!accessToken && !refreshToken) return true;
    await clearSessionTokensCore(storage);
    return true;
  });
}

async function logoutSession(storage, revoke) {
  const revocation = Promise.resolve().then(revoke).catch(() => {
    // Local credential deletion must not depend on network availability.
  });
  await clearSessionTokens(storage);
  void revocation;
}

async function restoreStoredSession(storage, request, isCurrent = () => true) {
  if (typeof isCurrent !== "function") throw new Error("A session-current predicate is required.");
  if (!isCurrent()) return null;
  const tokens = await readSessionTokens(storage, isCurrent);
  if (!isCurrent() || !tokens) return null;
  let activeTokens = tokens;
  try {
    try {
      const user = await request("/api/v1/auth/me", {}, tokens.accessToken);
      if (!isCurrent()) return null;
      return { tokens, user };
    } catch (error) {
      if (!isCurrent()) return null;
      if (error?.status !== 401) throw error;
    }
    if (!isCurrent()) return null;
    const result = await request("/api/v1/auth/refresh", {
      method: "POST",
      body: JSON.stringify({ refreshToken: tokens.refreshToken })
    });
    if (!isCurrent()) return null;
    const nextTokens = { accessToken: result.accessToken, refreshToken: result.refreshToken };
    const saved = await saveSessionTokensIfCurrent(storage, nextTokens, isCurrent);
    if (!saved || !isCurrent()) return null;
    activeTokens = nextTokens;
    const user = await request("/api/v1/auth/me", {}, nextTokens.accessToken);
    if (!isCurrent()) return null;
    return { tokens: nextTokens, user };
  } catch (error) {
    if (!isCurrent()) return null;
    if (error?.status === 400 || error?.status === 401) {
      await clearSessionTokensIfCurrent(storage, isCurrent, activeTokens);
      return null;
    }
    throw error;
  }
}

module.exports = {
  ACCESS_TOKEN_KEY,
  REFRESH_TOKEN_KEY,
  readSessionTokens,
  readAccessTokenIfCurrent,
  saveSessionTokens,
  saveSessionTokensIfCurrent,
  clearSessionTokens,
  logoutSession,
  restoreStoredSession
};
