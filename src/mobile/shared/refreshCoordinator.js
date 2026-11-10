function singleFlight(ref, operation) {
  if (ref.current === null) {
    const pending = Promise.resolve().then(operation);
    let shared;
    shared = pending.finally(() => {
      if (ref.current === shared) ref.current = null;
    });
    ref.current = shared;
  }
  return ref.current;
}

function getFreshAccessToken(ref, rejectedAccessToken, readTokens, refresh) {
  return singleFlight(ref, async () => {
    const tokens = await readTokens();
    if (tokens?.accessToken && tokens.accessToken !== rejectedAccessToken) {
      return tokens.accessToken;
    }
    if (typeof tokens?.refreshToken !== "string" || tokens.refreshToken.length === 0) {
      throw new Error("No refresh token available.");
    }
    return refresh(tokens.refreshToken);
  });
}

function advanceSessionEpoch(epochRef, inFlightRefreshRef) {
  if (!epochRef || !Number.isSafeInteger(epochRef.current) || epochRef.current < 0) {
    throw new Error("Invalid session epoch.");
  }

  epochRef.current += 1;
  if (inFlightRefreshRef) inFlightRefreshRef.current = null;
  return epochRef.current;
}

module.exports = { singleFlight, getFreshAccessToken, advanceSessionEpoch };
