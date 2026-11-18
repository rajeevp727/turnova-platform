async function readJsonResponseIfCurrent(response, isCurrent) {
  if (!response || typeof response.json !== "function" || typeof isCurrent !== "function") {
    throw new Error("A response and session-current predicate are required.");
  }
  if (!isCurrent()) throw new Error("Session changed. Please sign in again.");

  let body;
  try {
    body = await response.json();
  } catch {
    body = {};
  }

  if (!isCurrent()) throw new Error("Session changed. Please sign in again.");
  return body;
}

module.exports = { readJsonResponseIfCurrent };
