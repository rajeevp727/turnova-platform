"use strict";

function passengerCountInputFromDraft(value) {
  return Number.isInteger(value) && value >= 1 && value <= 6 ? String(value) : "";
}

function parsePassengerCountInput(value) {
  if (typeof value !== "string") return null;
  const normalized = value.trim();
  if (!/^[1-6]$/.test(normalized)) return null;
  return Number(normalized);
}

module.exports = { passengerCountInputFromDraft, parsePassengerCountInput };
