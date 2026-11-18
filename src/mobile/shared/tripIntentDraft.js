"use strict";

const ALLOWED_MISSING_FIELDS = Object.freeze(["pickup", "dropoff", "passengerCount"]);
const ALLOWED_SOURCES = new Set(["provider", "deterministic-fallback"]);

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function passengerCountInputFromDraft(value) {
  return Number.isInteger(value) && value >= 1 && value <= 6 ? String(value) : "";
}

function parsePassengerCountInput(value) {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (!/^[1-6]$/.test(normalized)) return null;
  return Number(normalized);
}

function normalizeRiderTripIntentDraft(value) {
  if (!isRecord(value)) return null;

  const validRouteText = item => item === null ||
    (typeof item === "string" && item.length > 0 && item.length <= 160 && item === item.trim());
  if (!validRouteText(value.pickup) || !validRouteText(value.dropoff)) return null;
  if (value.serviceClass !== "Cab") return null;
  if (!(value.passengerCount === null ||
      (Number.isInteger(value.passengerCount) && value.passengerCount >= 1 && value.passengerCount <= 6))) return null;
  if (typeof value.needsClarification !== "boolean" || value.requiresUserConfirmation !== true) return null;
  if (!Array.isArray(value.missingFields) ||
      value.missingFields.some(field => !ALLOWED_MISSING_FIELDS.includes(field)) ||
      new Set(value.missingFields).size !== value.missingFields.length) return null;
  if (!ALLOWED_SOURCES.has(value.source)) return null;

  const expectedMissingFields = [];
  if (value.pickup === null) expectedMissingFields.push("pickup");
  if (value.dropoff === null) expectedMissingFields.push("dropoff");
  if (value.passengerCount === null) expectedMissingFields.push("passengerCount");
  if (value.missingFields.length !== expectedMissingFields.length ||
      value.missingFields.some((field, index) => field !== expectedMissingFields[index]) ||
      value.needsClarification !== (expectedMissingFields.length > 0)) return null;

  // Project only the approved draft fields; ignore unrelated provider/transport metadata.
  return {
    pickup: value.pickup,
    dropoff: value.dropoff,
    serviceClass: "Cab",
    passengerCount: value.passengerCount,
    needsClarification: value.needsClarification,
    missingFields: expectedMissingFields,
    requiresUserConfirmation: true,
    source: value.source
  };
}

function ownsRiderTripIntentRequest(requestSequence, currentSequence, requestSessionEpoch, currentSessionEpoch) {
  return Number.isSafeInteger(requestSequence) && requestSequence > 0 &&
    Number.isSafeInteger(currentSequence) && requestSequence === currentSequence &&
    Number.isSafeInteger(requestSessionEpoch) && requestSessionEpoch >= 0 &&
    Number.isSafeInteger(currentSessionEpoch) && requestSessionEpoch === currentSessionEpoch;
}

function shouldApplyRiderTripIntentResponse(
  requestSequence,
  currentSequence,
  requestSessionEpoch,
  currentSessionEpoch,
  requestFormRevision,
  currentFormRevision
) {
  return ownsRiderTripIntentRequest(
    requestSequence, currentSequence, requestSessionEpoch, currentSessionEpoch
  ) &&
    Number.isSafeInteger(requestFormRevision) && requestFormRevision >= 0 &&
    Number.isSafeInteger(currentFormRevision) && requestFormRevision === currentFormRevision;
}

module.exports = {
  passengerCountInputFromDraft,
  parsePassengerCountInput,
  normalizeRiderTripIntentDraft,
  ownsRiderTripIntentRequest,
  shouldApplyRiderTripIntentResponse
};
