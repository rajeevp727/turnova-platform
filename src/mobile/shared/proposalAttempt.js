"use strict";

function createProposalIdempotencyKey(actor) {
  return `${actor}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function getOrCreateProposalAttempt(ref, input, keyFactory = createProposalIdempotencyKey) {
  if (!ref || typeof ref !== "object" || !("current" in ref)) {
    throw new TypeError("A proposal-attempt ref is required.");
  }
  if (!input || typeof input !== "object") {
    throw new TypeError("Proposal-attempt input is required.");
  }

  const { rideId, actor, amount, currency = "INR" } = input;
  if (typeof rideId !== "string" || !rideId.trim()) throw new TypeError("A ride ID is required.");
  if (actor !== "rider" && actor !== "driver") throw new TypeError("A supported proposal actor is required.");
  if (!Number.isFinite(amount) || amount <= 0) throw new TypeError("A positive proposal amount is required.");
  if (typeof currency !== "string" || !currency.trim()) throw new TypeError("A proposal currency is required.");
  if (typeof keyFactory !== "function") throw new TypeError("A proposal idempotency-key factory is required.");

  const fingerprint = JSON.stringify({ rideId, actor, amount, currency });
  if (ref.current?.fingerprint !== fingerprint) {
    const key = keyFactory(actor);
    if (typeof key !== "string" || !key.trim()) throw new TypeError("The proposal idempotency key must be a non-empty string.");
    ref.current = { fingerprint, key, rideId, actor, amount, currency };
  }

  return ref.current;
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

module.exports = { getOrCreateProposalAttempt, clearProposalAttempt, finishProposalSubmission };
