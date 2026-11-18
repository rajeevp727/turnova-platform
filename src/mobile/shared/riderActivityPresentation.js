"use strict";

const { agreementConfirmationLabel } = require("./agreementConfirmation");
const { formatAgreementAmount } = require("./agreementFormatting");

const RIDE_STATUSES = new Set([
  "Requested", "Matching", "Offered", "Negotiating", "AgreementConfirmed",
  "Accepted", "Arrived", "InProgress", "Completed", "Cancelled", "Expired"
]);

function displayText(value, fallback) {
  return typeof value === "string" && value.trim().length > 0 ? value.trim() : fallback;
}

function getRiderRideStatusLabel(status) {
  return typeof status === "string" && RIDE_STATUSES.has(status)
    ? status.toUpperCase()
    : "STATUS UNAVAILABLE";
}

function isRiderRideStatusEventValid(update, expectedRideId) {
  if (typeof expectedRideId !== "string" || expectedRideId.length === 0 || expectedRideId !== expectedRideId.trim()) return false;
  if (!update || typeof update !== "object" || Array.isArray(update)) return false;
  return update.rideId === expectedRideId &&
    typeof update.status === "string" &&
    update.status.length > 0 &&
    update.status.length <= 64 &&
    update.status === update.status.trim() &&
    !/[\u0000-\u001f\u007f]/.test(update.status);
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
    ". Status " + displayText(item?.status, "Unavailable") +
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
  return response.rideId === expectedRideId &&
    typeof response.status === "string" &&
    response.status === response.status.trim() &&
    RIDE_STATUSES.has(response.status);
}

module.exports = {
  getRiderRideStatusLabel,
  isRiderRideStatusEventValid,
  getRiderRideStatusMessage,
  getRiderHistoryAccessibilityLabel,
  getRiderAgreementVersionRouteLabel,
  getRiderAgreementVersionCreatedAtLabel,
  getRiderAgreementVersionAccessibilityLabel,
  isRiderCancellationResponseValid
};
