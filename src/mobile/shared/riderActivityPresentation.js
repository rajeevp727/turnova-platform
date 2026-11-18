"use strict";

const { agreementConfirmationLabel } = require("./agreementConfirmation");
const { formatAgreementAmount } = require("./agreementFormatting");

function displayText(value, fallback) {
  return typeof value === "string" && value.trim().length > 0 ? value : fallback;
}

function getRiderRideStatusMessage(ride) {
  const status = typeof ride?.status === "string" ? ride.status : "";
  switch (status) {
    case "Negotiating": {
      const expiryValue = typeof ride.negotiationExpiresAtUtc === "string"
        ? ride.negotiationExpiresAtUtc.trim()
        : "";
      const expiry = expiryValue ? new Date(expiryValue) : null;
      const deadline = expiry && Number.isFinite(expiry.getTime())
        ? "Negotiation expires at " + expiry.toLocaleTimeString() + "."
        : "A configured negotiation deadline applies.";
      return "Your selected driver is negotiating the fare with you. " + deadline;
    }
    case "AgreementConfirmed":
      return "Both parties confirmed the current fare. Waiting for the Driver to accept the trip.";
    case "Accepted":
      return "Driver accepted. Live location updates use SignalR.";
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
      return "Waiting for a driver to accept your request.";
  }
}

function getRiderHistoryAccessibilityLabel(item) {
  const requestedAt = typeof item?.requestedAt === "string" ? new Date(item.requestedAt) : null;
  const requestedAtLabel = requestedAt && Number.isFinite(requestedAt.getTime())
    ? requestedAt.toLocaleString()
    : "Date unavailable";
  const passengers = Number.isInteger(item?.passengers) && item.passengers >= 0
    ? item.passengers
    : "Unknown";
  return "Trip from " + displayText(item?.pickup, "Pickup unavailable") +
    " to " + displayText(item?.dropoff, "Drop-off unavailable") +
    ". Status " + displayText(item?.status, "Unavailable") +
    ". " + passengers + " passengers. Service class " +
    displayText(item?.serviceClass, "Unknown").toUpperCase() +
    ". Requested " + requestedAtLabel +
    ". Reference " + displayText(item?.rideId, "Unavailable") + ".";
}

function getRiderAgreementVersionAccessibilityLabel(item) {
  const version = Number.isInteger(item?.version) && item.version > 0 ? item.version : "Unavailable";
  const proposedBy = displayText(item?.proposedBy, "Unknown");
  const riderConfirmation = agreementConfirmationLabel(
    "Rider",
    item?.riderConfirmed === true,
    item?.riderConfirmedAtUtc
  );
  const driverConfirmation = agreementConfirmationLabel(
    "Driver",
    item?.driverConfirmed === true,
    item?.driverConfirmedAtUtc
  );
  return "Agreement version " + version + ". " +
    formatAgreementAmount(item?.amount, item?.currency) +
    ". Proposed by " + proposedBy + ". " +
    riderConfirmation + ". " + driverConfirmation + ".";
}

function isRiderCancellationResponseValid(response, expectedRideId) {
  if (typeof expectedRideId !== "string" || expectedRideId.length === 0 || expectedRideId !== expectedRideId.trim()) {
    return false;
  }
  if (!response || typeof response !== "object" || Array.isArray(response)) return false;
  return response.rideId === expectedRideId &&
    typeof response.status === "string" &&
    response.status.trim().length > 0;
}

module.exports = {
  getRiderRideStatusMessage,
  getRiderHistoryAccessibilityLabel,
  getRiderAgreementVersionAccessibilityLabel,
  isRiderCancellationResponseValid
};
