const INITIAL_RETRY_DELAY_MS = 1000;
const MAX_RETRY_DELAY_MS = 30000;
const MAX_RETRY_EXPONENT = 5;

function getSignalRReconnectDelayMs(previousRetryCount) {
  if (!Number.isInteger(previousRetryCount) || previousRetryCount < 0) {
    throw new RangeError("previousRetryCount must be a non-negative integer.");
  }

  return Math.min(
    MAX_RETRY_DELAY_MS,
    INITIAL_RETRY_DELAY_MS * (2 ** Math.min(previousRetryCount, MAX_RETRY_EXPONENT))
  );
}

function getJwtExpiryMilliseconds(accessToken) {
  if (typeof accessToken !== "string") return null;
  const segments = accessToken.split(".");
  if (segments.length !== 3 || !segments[1]) return null;

  const decodeBase64 = globalThis?.atob;
  if (typeof decodeBase64 !== "function") return null;

  try {
    const base64 = segments[1].replace(/-/g, "+").replace(/_/g, "/");
    const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
    const payload = JSON.parse(decodeBase64(padded));
    if (!Number.isFinite(payload?.exp) || payload.exp <= 0) return null;
    return payload.exp * 1000;
  } catch {
    return null;
  }
}

async function getSignalRAccessToken({ readAccessToken, isCurrent, validateSession, now = Date.now }) {
  if (typeof readAccessToken !== "function" ||
      typeof isCurrent !== "function" ||
      typeof validateSession !== "function" ||
      typeof now !== "function") {
    throw new TypeError("SignalR token access requires read, current-session, validation and clock functions.");
  }

  if (!isCurrent()) return "";
  const accessToken = await readAccessToken();
  if (!isCurrent()) return "";
  if (typeof accessToken !== "string" || accessToken.length === 0) return "";

  const expiresAt = getJwtExpiryMilliseconds(accessToken);
  if (expiresAt !== null && expiresAt > now()) return accessToken;

  // An expired or locally unverifiable token is checked through the existing
  // authenticated /me path. That path owns the 401 refresh flow and its epoch
  // fencing; this helper only re-reads the resulting token for SignalR.
  await validateSession();
  if (!isCurrent()) return "";

  const validatedToken = await readAccessToken();
  if (!isCurrent()) return "";
  return typeof validatedToken === "string" ? validatedToken : "";
}

const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

async function startSignalRConnection(connection, shouldContinue, onRetry = () => {}, waitForRetry = wait) {
  let previousRetryCount = 0;
  while (shouldContinue()) {
    try {
      await connection.start();
      if (!shouldContinue()) {
        try { await connection.stop?.(); } catch { /* Session invalidation wins over transport shutdown errors. */ }
        return false;
      }
      return true;
    } catch {
      if (!shouldContinue()) return false;
      onRetry();
      await waitForRetry(getSignalRReconnectDelayMs(previousRetryCount));
      previousRetryCount += 1;
    }
  }

  return false;
}

module.exports = { getJwtExpiryMilliseconds, getSignalRAccessToken, getSignalRReconnectDelayMs, startSignalRConnection };
