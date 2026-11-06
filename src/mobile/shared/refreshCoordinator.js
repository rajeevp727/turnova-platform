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

module.exports = { singleFlight, getFreshAccessToken };
