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

module.exports = { getSignalRReconnectDelayMs, startSignalRConnection };
