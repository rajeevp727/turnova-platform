function stopRegisteredConnections(stopCallbacks) {
  const callbacks = Array.from(stopCallbacks);
  stopCallbacks.clear();
  for (const stop of callbacks) {
    try {
      const result = stop();
      if (result && typeof result.then === "function") void Promise.resolve(result).catch(() => undefined);
    } catch {
      // Session invalidation must not wait for or be blocked by transport shutdown.
    }
  }
}

module.exports = { stopRegisteredConnections };
