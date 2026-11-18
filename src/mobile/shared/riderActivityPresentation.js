"use strict";

const { agreementConfirmationLabel } = require("./agreementConfirmation");
const { formatAgreementAmount } = require("./agreementFormatting");

const AGREEMENT_TIMESTAMP_WITH_OFFSET = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/i;

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isSafeAgreementText(value, maximumLength = 250) {
  return typeof value === "string" &&
    value.length > 0 &&
    value.length <= maximumLength &&
    value === value.trim() &&
    !/[\u0000-\u001f\u007f-\u009f]/.test(value);
}

function normalizeAgreementTimestamp(value) {
  return isSafeAgreementText(value, 64) &&
    AGREEMENT_TIMESTAMP_WITH_OFFSET.test(value) &&
    Number.isFinite(Date.parse(value))
    ? value
    : null;
}

function normalizeOptionalAgreementTimestamp(value) {
  if (value === null) return null;
  return normalizeAgreementTimestamp(value);
}

function normalizeRiderAgreementHistory(value, expectedRideId) {
  if (!Array.isArray(value) || !isSafeAgreementText(expectedRideId, 128)) return null;
  const result = [];
  let previousVersion = 0;

  for (const item of value) {
    if (!isRecord(item) ||
        item.rideId !== expectedRideId ||
        !Number.isSafeInteger(item.version) ||
        item.version <= previousVersion ||
        !Number.isFinite(item.amount) ||
        item.amount <= 0 ||
        Math.abs(item.amount * 100 - Math.round(item.amount * 100)) > 0.000001 ||
        typeof item.currency !== "string" ||
        !/^[A-Z]{3}$/.test(item.currency) ||
        !isSafeAgreementText(item.pickup) ||
        !isSafeAgreementText(item.dropoff) ||
        !Array.isArray(item.stops) ||
        item.stops.length > 10 ||
        item.stops.some(stop => !isSafeAgreementText(stop)) ||
        !["Rider", "Driver"].includes(item.proposedBy) ||
        typeof item.riderConfirmed !== "boolean" ||
        typeof item.driverConfirmed !== "boolean") return null;

    const createdAtUtc = normalizeAgreementTimestamp(item.createdAtUtc);
    const riderConfirmedAtUtc = normalizeOptionalAgreementTimestamp(item.riderConfirmedAtUtc);
    const driverConfirmedAtUtc = normalizeOptionalAgreementTimestamp(item.driverConfirmedAtUtc);
    if (!createdAtUtc ||
        (item.riderConfirmedAtUtc !== null && !riderConfirmedAtUtc) ||
        (item.driverConfirmedAtUtc !== null && !driverConfirmedAtUtc) ||
        (item.riderConfirmed && !riderConfirmedAtUtc) ||
        (!item.riderConfirmed && item.riderConfirmedAtUtc !== null) ||
        (item.driverConfirmed && !driverConfirmedAtUtc) ||
        (!item.driverConfirmed && item.driverConfirmedAtUtc !== null) ||
        (riderConfirmedAtUtc && Date.parse(riderConfirmedAtUtc) < Date.parse(createdAtUtc)) ||
        (driverConfirmedAtUtc && Date.parse(driverConfirmedAtUtc) < Date.parse(createdAtUtc))) return null;

    result.push({
      rideId: expectedRideId,
      version: item.version,
      amount: item.amount,
      currency: item.currency,
      pickup: item.pickup,
      dropoff: item.dropoff,
      stops: [...item.stops],
      proposedBy: item.proposedBy,
      createdAtUtc,
      riderConfirmed: item.riderConfirmed,
      riderConfirmedAtUtc,
      driverConfirmed: item.driverConfirmed,
      driverConfirmedAtUtc
    });
    previousVersion = item.version;
  }

  return result;
}

function shouldApplyRiderAgreementHistoryResponse(requestSequence, currentSequence) {
  return Number.isSafeInteger(requestSequence) &&
    requestSequence > 0 &&
    Number.isSafeInteger(currentSequence) &&
    requestSequence === currentSequence;
}

function displayText(value, fallback) {
  return typeof value === "string" && value.trim().length > 0 ? value.trim() : fallback;
}

const RIDE_STATUS_LABELS = new Map([
  ["Requested", "REQUESTED"],
  ["Matching", "FINDING DRIVER"],
  ["Offered", "WAITING FOR DRIVER"],
  ["Negotiating", "NEGOTIATING"],
  ["AgreementConfirmed", "AGREEMENT CONFIRMED"],
  ["Accepted", "DRIVER ACCEPTED"],
  ["Arrived", "DRIVER ARRIVED"],
  ["InProgress", "IN PROGRESS"],
  ["Completed", "COMPLETED"],
  ["Cancelled", "CANCELLED"],
  ["Expired", "EXPIRED"]
]);

function getRiderRideStatusLabel(status) {
  return RIDE_STATUS_LABELS.get(status) ?? "STATUS UNAVAILABLE";
}

function isRiderRideAgreementEventValid(update, expectedRideId) {
  return isSafeAgreementText(expectedRideId, 128) &&
    isRecord(update) &&
    update.rideId === expectedRideId;
}

function isRiderRideStatusEventValid(update, expectedRideId) {
  if (typeof expectedRideId !== "string" || expectedRideId.length === 0 || expectedRideId !== expectedRideId.trim()) return false;
  if (!update || typeof update !== "object" || Array.isArray(update)) return false;
  return update.rideId === expectedRideId &&
    typeof update.status === "string" &&
    update.status.length > 0 &&
    update.status.length <= 64 &&
    update.status === update.status.trim() &&
    !/[\u0000-\u001f\u007f-\u009f]/.test(update.status);
}

function getRiderRideStatusMessage(ride) {
  const status = typeof ride?.status === "string" ? ride.status : "";
  switch (status) {
    case "Requested":
    case "Matching":
    case "Offered":
      return "Your ride request is waiting for a Driver response.";
    case "Negotiating": {
      const expiryValue = typeof ride.negotiationExpiresAtUtc === "string" ? ride.negotiationExpiresAtUtc.trim() : "";
      const expiry = expiryValue ? new Date(expiryValue) : null;
      const deadline = expiry && Number.isFinite(expiry.getTime())
        ? "Negotiation expires at " + expiry.toLocaleTimeString() + "."
        : "Negotiation deadline is unavailable.";
      return "Your selected driver is negotiating the fare with you. " + deadline;
    }
    case "AgreementConfirmed":
      return "Both parties confirmed the current fare. Waiting for the Driver to accept the trip.";
    case "Accepted":
      return "Driver accepted. Location updates appear while the ride is active and the connection is available.";
    case "Arrived":
      return "Your driver has arrived.";
    case "InProgress":
      return "Your ride is in progress.";
    case "Completed":
      return "Ride completed.";
    case "Expired":
      return "The selected-driver negotiation expired. Choose another available Driver.";
    case "Cancelled":
      return "Ride cancelled.";
    default:
      return "Ride status is unavailable. Refresh Activity to check the latest status.";
  }
}

function getRiderHistoryAccessibilityLabel(item) {
  const requestedAt = typeof item?.requestedAt === "string" ? new Date(item.requestedAt) : null;
  const requestedAtLabel = requestedAt && Number.isFinite(requestedAt.getTime()) ? requestedAt.toLocaleString() : "Date unavailable";
  const passengers = Number.isInteger(item?.passengers) && item.passengers >= 0 ? item.passengers : "Unknown";
  return "Trip from " + displayText(item?.pickup, "Pickup unavailable") +
    " to " + displayText(item?.dropoff, "Drop-off unavailable") +
    ". Status " + getRiderRideStatusLabel(item?.status) +
    ". " + passengers + " passengers. Service class " +
    displayText(item?.serviceClass, "Unknown").toUpperCase() +
    ". Requested " + requestedAtLabel +
    ". Reference " + displayText(item?.rideId, "Unavailable") + ".";
}

function getRiderAgreementVersionRouteLabel(item) {
  const pickup = displayText(item?.pickup, "Pickup unavailable");
  const dropoff = displayText(item?.dropoff, "Drop-off unavailable");
  const stops = Array.isArray(item?.stops)
    ? item.stops.filter(stop => typeof stop === "string" && stop.trim().length > 0).map(stop => stop.trim())
    : [];
  return "Route: " + pickup + " to " + dropoff + (stops.length > 0 ? ". Stops: " + stops.join(", ") : "") + ".";
}

function getRiderAgreementVersionCreatedAtLabel(item) {
  const value = typeof item?.createdAtUtc === "string" ? item.createdAtUtc.trim() : "";
  const createdAt = value ? new Date(value) : null;
  return createdAt && Number.isFinite(createdAt.getTime())
    ? "Created " + createdAt.toLocaleString() + "."
    : "Creation time unavailable.";
}

function getRiderAgreementVersionAccessibilityLabel(item) {
  const version = Number.isInteger(item?.version) && item.version > 0 ? item.version : "Unavailable";
  const proposedBy = displayText(item?.proposedBy, "Unknown");
  const riderConfirmation = agreementConfirmationLabel("Rider", item?.riderConfirmed === true, item?.riderConfirmedAtUtc);
  const driverConfirmation = agreementConfirmationLabel("Driver", item?.driverConfirmed === true, item?.driverConfirmedAtUtc);
  return "Agreement version " + version + ". " + formatAgreementAmount(item?.amount, item?.currency) + ". " +
    getRiderAgreementVersionRouteLabel(item) + " " + getRiderAgreementVersionCreatedAtLabel(item) + " " +
    "Proposed by " + proposedBy + ". " + riderConfirmation + ". " + driverConfirmation + ".";
}

function isRiderCancellationResponseValid(response, expectedRideId) {
  if (typeof expectedRideId !== "string" || expectedRideId.length === 0 || expectedRideId !== expectedRideId.trim()) return false;
  if (!response || typeof response !== "object" || Array.isArray(response)) return false;
  return response.rideId === expectedRideId && response.status === "Cancelled";
}

module.exports = {
  getRiderRideStatusLabel,
  isRiderRideAgreementEventValid,
  isRiderRideStatusEventValid,
  getRiderRideStatusMessage,
  getRiderHistoryAccessibilityLabel,
  getRiderAgreementVersionRouteLabel,
  getRiderAgreementVersionCreatedAtLabel,
  getRiderAgreementVersionAccessibilityLabel,
  isRiderCancellationResponseValid,
  normalizeRiderAgreementHistory,
  shouldApplyRiderAgreementHistoryResponse
};
