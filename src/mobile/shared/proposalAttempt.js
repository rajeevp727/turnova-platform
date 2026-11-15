"use strict";

const PROPOSAL_ATTEMPT_STORAGE_PREFIX = "turnova.proposalAttempt.v1";
const proposalStorageQueues = new WeakMap();

function createProposalIdempotencyKey(actor) {
  return actor + "-" + Date.now() + "-" + Math.random().toString(36).slice(2);
}

function validateProposalAttemptInput(input, requireUserId = false) {
  if (!input || typeof input !== "object") throw new TypeError("Proposal-attempt input is required.");

  const { rideId, actor, amount, currency = "INR" } = input;
  const userId = input.userId ?? null;
  if (typeof rideId !== "string" || !rideId.trim()) throw new TypeError("A ride ID is required.");
  if (actor !== "rider" && actor !== "driver") throw new TypeError("A supported proposal actor is required.");
  if (!Number.isFinite(amount) || amount <= 0) throw new TypeError("A positive proposal amount is required.");
  if (typeof currency !== "string" || !currency.trim()) throw new TypeError("A proposal currency is required.");
  if (requireUserId && (typeof userId !== "string" || !userId.trim())) {
    throw new TypeError("A user ID is required for a persistent proposal attempt.");
  }
  if (userId !== null && (typeof userId !== "string" || !userId.trim())) {
    throw new TypeError("A proposal user ID must be a non-empty string.");
  }
  if (typeof userId === "string" && userId.length > 256) throw new TypeError("The proposal user ID is too long.");
  if (rideId.length > 256) throw new TypeError("The proposal ride ID is too long.");

  return { rideId, actor, userId, amount, currency };
}

function proposalFingerprint(input) {
  return JSON.stringify({
    rideId: input.rideId,
    actor: input.actor,
    userId: input.userId ?? null,
    amount: input.amount,
    currency: input.currency ?? "INR"
  });
}

function createProposalAttemptStorageKey(input) {
  const normalized = validateProposalAttemptInput(input, true);
  return PROPOSAL_ATTEMPT_STORAGE_PREFIX + "." + normalized.actor + "." +
    encodeURIComponent(normalized.userId);
}

function getOrCreateProposalAttempt(ref, input, keyFactory = createProposalIdempotencyKey) {
  if (!ref || typeof ref !== "object" || !("current" in ref)) {
    throw new TypeError("A proposal-attempt ref is required.");
  }
  const normalized = validateProposalAttemptInput(input);
  if (typeof keyFactory !== "function") throw new TypeError("A proposal idempotency-key factory is required.");

  const fingerprint = proposalFingerprint(normalized);
  if (ref.current?.fingerprint !== fingerprint) {
    const key = keyFactory(normalized.actor);
    if (typeof key !== "string" || !key.trim()) throw new TypeError("The proposal idempotency key must be a non-empty string.");
    ref.current = { fingerprint, key, ...normalized };
  }

  return ref.current;
}

function validateProposalStorage(storage) {
  if (
    !storage ||
    typeof storage !== "object" ||
    typeof storage.getItemAsync !== "function" ||
    typeof storage.setItemAsync !== "function" ||
    typeof storage.deleteItemAsync !== "function"
  ) {
    throw new TypeError("A compatible secure proposal-attempt store is required.");
  }
}

function serializeProposalStorage(storage, operation) {
  const previous = proposalStorageQueues.get(storage) ?? Promise.resolve();
  const current = previous.catch(() => undefined).then(operation);
  proposalStorageQueues.set(storage, current.catch(() => undefined));
  return current;
}

function parseStoredProposalAttempt(raw) {
  if (raw === null || raw === undefined) return { status: "missing", attempt: null };
  if (typeof raw !== "string" || !raw.trim()) return { status: "invalid", attempt: null };
  try {
    const envelope = JSON.parse(raw);
    if (envelope?.version !== 1 || !envelope.attempt || typeof envelope.attempt !== "object") {
      return { status: "invalid", attempt: null };
    }
    const attempt = envelope.attempt;
    const normalized = validateProposalAttemptInput(attempt, true);
    if (typeof attempt.key !== "string" || !attempt.key.trim()) return { status: "invalid", attempt: null };
    const fingerprint = proposalFingerprint(normalized);
    if (attempt.fingerprint !== fingerprint) return { status: "invalid", attempt: null };
    return { status: "valid", attempt: { ...normalized, fingerprint, key: attempt.key } };
  } catch {
    return { status: "invalid", attempt: null };
  }
}

async function getOrCreatePersistentProposalAttempt(
  storage,
  ref,
  input,
  keyFactory = createProposalIdempotencyKey,
  isCurrent = () => true
) {
  validateProposalStorage(storage);
  if (!ref || typeof ref !== "object" || !("current" in ref)) {
    throw new TypeError("A proposal-attempt ref is required.");
  }
  if (typeof isCurrent !== "function") throw new TypeError("A proposal-current predicate is required.");
  const normalized = validateProposalAttemptInput(input, true);
  const fingerprint = proposalFingerprint(normalized);
  const storageKey = createProposalAttemptStorageKey(normalized);

  return serializeProposalStorage(storage, async () => {
    if (!isCurrent()) throw new Error("Session changed before the proposal retry key could be restored.");
    let raw;
    try {
      raw = await storage.getItemAsync(storageKey);
    } catch {
      throw new Error("Unable to securely read the pending proposal retry key. No proposal was sent.");
    }
    if (!isCurrent()) throw new Error("Session changed before the proposal retry key could be used.");

    const storedRecord = parseStoredProposalAttempt(raw);
    if (storedRecord.status === "invalid") {
      throw new Error("Stored proposal retry data is unreadable. No proposal was sent; refresh agreement history and resolve secure storage before retrying.");
    }
    if (storedRecord.status === "valid" &&
      (storedRecord.attempt.userId !== normalized.userId || storedRecord.attempt.actor !== normalized.actor)) {
      throw new Error("Stored proposal retry data belongs to another account or actor. No proposal was sent.");
    }
    if (storedRecord.status === "valid" && storedRecord.attempt.fingerprint === fingerprint) {
      ref.current = storedRecord.attempt;
      return storedRecord.attempt;
    }

    const attempt = ref.current?.fingerprint === fingerprint
      ? ref.current
      : getOrCreateProposalAttempt(ref, normalized, keyFactory);
    if (!isCurrent()) throw new Error("Session changed before the proposal retry key could be saved.");

    try {
      await storage.setItemAsync(storageKey, JSON.stringify({ version: 1, attempt }));
    } catch {
      throw new Error("Unable to securely persist the proposal retry key. No proposal was sent.");
    }
    if (!isCurrent()) throw new Error("Session changed before the proposal could be sent.");

    ref.current = attempt;
    return attempt;
  });
}

async function clearPersistentProposalAttempt(storage, ref, expectedAttempt) {
  validateProposalStorage(storage);
  if (!ref || typeof ref !== "object" || !("current" in ref)) {
    throw new TypeError("A proposal-attempt ref is required.");
  }
  const normalized = validateProposalAttemptInput(expectedAttempt, true);
  if (typeof expectedAttempt.key !== "string" || !expectedAttempt.key.trim()) {
    throw new TypeError("The expected proposal idempotency key is required.");
  }
  const fingerprint = proposalFingerprint(normalized);
  if (expectedAttempt.fingerprint !== fingerprint) throw new TypeError("The expected proposal attempt is inconsistent.");
  const storageKey = createProposalAttemptStorageKey(normalized);

  return serializeProposalStorage(storage, async () => {
    let raw;
    try {
      raw = await storage.getItemAsync(storageKey);
    } catch {
      throw new Error("Unable to securely read the completed proposal retry key.");
    }
    const storedRecord = parseStoredProposalAttempt(raw);
    if (storedRecord.status === "missing") {
      if (ref.current?.key === expectedAttempt.key) ref.current = null;
      return false;
    }
    if (storedRecord.status === "valid" &&
      (storedRecord.attempt.fingerprint !== fingerprint || storedRecord.attempt.key !== expectedAttempt.key)) {
      return false;
    }
    if (storedRecord.status === "invalid" && ref.current?.key !== expectedAttempt.key) return false;

    try {
      await storage.deleteItemAsync(storageKey);
    } catch {
      throw new Error("Unable to securely clear the completed proposal retry key.");
    }
    if (ref.current?.key === expectedAttempt.key) ref.current = null;
    return true;
  });
}

function clearProposalAttempt(ref, expectedKey) {
  if (!ref || !ref.current || ref.current.key !== expectedKey) return false;
  ref.current = null;
  return true;
}

function finishProposalSubmission(ref, expectedKey) {
  if (!ref || typeof ref !== "object" || !("current" in ref)) {
    throw new TypeError("A proposal-submission ref is required.");
  }
  if (typeof expectedKey !== "string" || !expectedKey.trim()) {
    throw new TypeError("The expected proposal key is required.");
  }
  if (ref.current !== expectedKey) return false;
  ref.current = null;
  return true;
}

module.exports = {
  PROPOSAL_ATTEMPT_STORAGE_PREFIX,
  createProposalAttemptStorageKey,
  getOrCreateProposalAttempt,
  getOrCreatePersistentProposalAttempt,
  clearProposalAttempt,
  clearPersistentProposalAttempt,
  finishProposalSubmission
};
