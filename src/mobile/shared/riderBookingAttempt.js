"use strict";

function validateLock(inFlight) {
  if (!inFlight || typeof inFlight !== "object" || !("current" in inFlight)) {
    throw new TypeError("A Rider booking attempt lock is required.");
  }
}

function validateOwner(owner) {
  if (typeof owner !== "string" || owner.length === 0 || owner !== owner.trim() ||
      owner.length > 256 || /[\\u0000-\\u001f\\u007f-\\u009f]/.test(owner)) {
    throw new TypeError("A valid Rider booking attempt owner is required.");
  }
}

function tryBeginRiderBookingAttempt(inFlight, owner) {
  validateLock(inFlight);
  validateOwner(owner);
  if (inFlight.current !== null) return false;
  inFlight.current = owner;
  return true;
}

function finishRiderBookingAttempt(inFlight, owner) {
  validateLock(inFlight);
  validateOwner(owner);
  if (inFlight.current !== owner) return false;
  inFlight.current = null;
  return true;
}

module.exports = { finishRiderBookingAttempt, tryBeginRiderBookingAttempt };
