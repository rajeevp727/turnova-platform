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

module.exports = { singleFlight };
