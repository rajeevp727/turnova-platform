"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  getRiderAgreementVersionAccessibilityLabel,
  getRiderHistoryAccessibilityLabel,
  getRiderRideStatusMessage,
  isRiderCancellationResponseValid
} = require("./riderActivityPresentation.js");

test("Rider Activity status presentation covers the server lifecycle without inventing payment or dispatch outcomes", () => {
  const expected = new Map([
    ["Requested", "Waiting for a driver to accept your request."],
    ["Matching", "Waiting for a driver to accept your request."],
    ["Offered", "Waiting for a driver to accept your request."],
    ["AgreementConfirmed", "Both parties confirmed the current fare. Waiting for the Driver to accept the trip."],
    ["Accepted", "Driver accepted. Live location updates use SignalR."],
    ["Arrived", "Your driver has arrived."],
    ["InProgress", "Your ride is in progress."],
    ["Completed", "Ride completed."],
    ["Expired", "The selected-driver negotiation expired. Choose another available Driver."],
    ["Cancelled", "Ride cancelled."]
  ]);
  for (const [status, message] of expected) {
    assert.equal(getRiderRideStatusMessage({ status }), message, "Unexpected status message for " + status);
  }
});

test("Rider negotiation status announces a valid deadline and fails safely for invalid timestamps", () => {
  const expiry = "2026-11-18T12:30:00.000Z";
  assert.equal(
    getRiderRideStatusMessage({ status: "Negotiating", negotiationExpiresAtUtc: expiry }),
    "Your selected driver is negotiating the fare with you. Negotiation expires at " + new Date(expiry).toLocaleTimeString() + "."
  );
  assert.equal(
    getRiderRideStatusMessage({ status: "Negotiating", negotiationExpiresAtUtc: "not-a-date" }),
    "Your selected driver is negotiating the fare with you. A configured negotiation deadline applies."
  );
  assert.equal(
    getRiderRideStatusMessage({ status: "Negotiating", negotiationExpiresAtUtc: null }),
    "Your selected driver is negotiating the fare with you. A configured negotiation deadline applies."
  );
});

test("Rider trip-history accessibility summary includes route, status, passenger count, service class and reference", () => {
  const item = {
    rideId: "ride-123",
    pickup: "Nizampet",
    dropoff: "Gachibowli",
    passengers: 2,
    serviceClass: "cab",
    status: "Completed",
    requestedAt: "2026-11-18T10:15:00.000Z"
  };
  assert.equal(
    getRiderHistoryAccessibilityLabel(item),
    "Trip from Nizampet to Gachibowli. Status Completed. 2 passengers. Service class CAB. Requested " +
      new Date(item.requestedAt).toLocaleString() + ". Reference ride-123."
  );
});

test("Rider trip-history accessibility summary handles missing display values without crashing", () => {
  assert.equal(
    getRiderHistoryAccessibilityLabel({ rideId: "", pickup: "", dropoff: "", passengers: null, serviceClass: "", status: "", requestedAt: "bad-date" }),
    "Trip from Pickup unavailable to Drop-off unavailable. Status Unavailable. Unknown passengers. Service class UNKNOWN. Requested Date unavailable. Reference Unavailable."
  );
});

test("Rider agreement-version accessibility summary exposes immutable version, currency and both confirmations", () => {
  const confirmedAt = "2026-11-18T10:15:00.000Z";
  const label = getRiderAgreementVersionAccessibilityLabel({
    version: 3,
    amount: 425,
    currency: "inr",
    proposedBy: "Driver",
    riderConfirmed: false,
    riderConfirmedAtUtc: null,
    driverConfirmed: true,
    driverConfirmedAtUtc: confirmedAt
  });
  assert.match(label, /^Agreement version 3\./);
  assert.match(label, /INR 425\.00/);
  assert.match(label, /Proposed by Driver/);
  assert.match(label, /Rider: Pending/);
  assert.match(label, new RegExp("Driver: Confirmed at " + new Date(confirmedAt).toLocaleString().replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
});

test("Rider cancellation response validation requires the exact ride and a non-empty status", () => {
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Cancelled" }, "ride-123"), true);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Requested" }, "ride-123"), true);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-other", status: "Cancelled" }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "" }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "   " }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: 7 }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid(null, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid([], "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Cancelled" }, " ride-123"), false);
});

test("Rider Activity screen wires tested status, agreement, history and cancellation presentation helpers", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.ok(source.includes('getRiderRideStatusMessage, isRiderCancellationResponseValid'));
  assert.ok(source.includes("{getRiderRideStatusMessage(ride)}"));
  assert.ok(source.includes("accessibilityLabel={getRiderAgreementVersionAccessibilityLabel(item)}"));
  assert.ok(source.includes("accessibilityLabel={getRiderHistoryAccessibilityLabel(item)}"));
  assert.ok(source.includes("isRiderCancellationResponseValid(updated,rideId)"));
});
