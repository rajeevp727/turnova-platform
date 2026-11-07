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

function clearSessionTokens(storage) {
  return serializeMutation(storage, () => clearSessionTokensCore(storage));
}

async function saveSessionTokens(storage, tokens) {
  if (
    !tokens ||
    typeof tokens.accessToken !== "string" ||
    tokens.accessToken.length === 0 ||
    typeof tokens.refreshToken !== "string" ||
    tokens.refreshToken.length === 0
  ) {
    throw new Error("Invalid session tokens.");
  }

  return serializeMutation(storage, async () => {
    try {
      await storage.setItemAsync(ACCESS_TOKEN_KEY, tokens.accessToken);
      await storage.setItemAsync(REFRESH_TOKEN_KEY, tokens.refreshToken);
    } catch {
      try {
        await clearSessionTokensCore(storage);
      } catch {
        // Preserve the save failure; callers surface a recoverable storage error.
      }
      throw new Error("Unable to securely save session tokens.");
    }
  });
}

async function readSessionTokens(storage) {
  const [accessToken, refreshToken] = await Promise.all([
    storage.getItemAsync(ACCESS_TOKEN_KEY),
    storage.getItemAsync(REFRESH_TOKEN_KEY)
  ]);

  if (accessToken && refreshToken) {
    return { accessToken, refreshToken };
  }

  if (accessToken || refreshToken) {
    await clearSessionTokens(storage);
  }

  return null;
}

async function logoutSession(storage, revoke) {
  const revocation = Promise.resolve().then(revoke).catch(() => {
    // Local credential deletion must not depend on network availability.
  });
  await clearSessionTokens(storage);
  void revocation;
}

async function restoreStoredSession(storage, request) {
  const tokens = await readSessionTokens(storage);
  if (!tokens) return null;

  try {
    const user = await request("/api/v1/auth/me", {}, tokens.accessToken);
    return { tokens, user };
  } catch (error) {
    if (error?.status !== 401) throw error;
  }

  try {
    const result = await request("/api/v1/auth/refresh", {
      method: "POST",
      body: JSON.stringify({ refreshToken: tokens.refreshToken })
    });
    const nextTokens = {
      accessToken: result.accessToken,
      refreshToken: result.refreshToken
    };
    await saveSessionTokens(storage, nextTokens);
    return { tokens: nextTokens, user: result.user };
  } catch (error) {
    if (error?.status === 400 || error?.status === 401) {
      await clearSessionTokens(storage);
      return null;
    }
    throw error;
  }
}

module.exports = {
  ACCESS_TOKEN_KEY,
  REFRESH_TOKEN_KEY,
  readSessionTokens,
  saveSessionTokens,
  clearSessionTokens,
  logoutSession,
  restoreStoredSession
};
