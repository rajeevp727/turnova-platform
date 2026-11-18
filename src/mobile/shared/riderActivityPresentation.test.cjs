"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  getRiderAgreementVersionAccessibilityLabel,
  getRiderRideStatusLabel,
  isRiderRideStatusEventValid,
  getRiderAgreementVersionCreatedAtLabel,
  getRiderAgreementVersionRouteLabel,
  getRiderHistoryAccessibilityLabel,
  getRiderRideStatusMessage,
  isRiderCancellationResponseValid
} = require("./riderActivityPresentation.js");

test("Rider Activity status presentation covers the server lifecycle without inventing payment or dispatch outcomes", () => {
  const expected = new Map([
    ["Requested", "Your ride request is waiting for a Driver response."],
    ["Matching", "Your ride request is waiting for a Driver response."],
    ["Offered", "Your ride request is waiting for a Driver response."],
    ["AgreementConfirmed", "Both parties confirmed the current fare. Waiting for the Driver to accept the trip."],
    ["Accepted", "Driver accepted. Location updates appear while the ride is active and the connection is available."],
    ["Arrived", "Your driver has arrived."],
    ["InProgress", "Your ride is in progress."],
    ["Completed", "Ride completed."],
    ["Expired", "The selected-driver negotiation expired. Choose another available Driver."],
    ["Cancelled", "Ride cancelled."]
  ]);
  for (const [status, message] of expected) {
    assert.equal(getRiderRideStatusMessage({ status }), message, "Unexpected status message for " + status);
  }
  assert.equal(getRiderRideStatusMessage({ status: "UnexpectedFutureStatus" }), "Ride status is unavailable. Refresh Activity to check the latest status.");
});

test("Rider negotiation status announces a valid deadline and fails safely for invalid timestamps", () => {
  const expiry = "2026-11-18T12:30:00.000Z";
  assert.equal(
    getRiderRideStatusMessage({ status: "Negotiating", negotiationExpiresAtUtc: expiry }),
    "Your selected driver is negotiating the fare with you. Negotiation expires at " + new Date(expiry).toLocaleTimeString() + "."
  );
  assert.equal(
    getRiderRideStatusMessage({ status: "Negotiating", negotiationExpiresAtUtc: "not-a-date" }),
    "Your selected driver is negotiating the fare with you. Negotiation deadline is unavailable."
  );
  assert.equal(
    getRiderRideStatusMessage({ status: "Negotiating", negotiationExpiresAtUtc: null }),
    "Your selected driver is negotiating the fare with you. Negotiation deadline is unavailable."
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
    "Trip from Nizampet to Gachibowli. Status COMPLETED. 2 passengers. Service class CAB. Requested " +
      new Date(item.requestedAt).toLocaleString() + ". Reference ride-123."
  );
});

test("Rider trip-history accessibility summary handles missing display values without crashing", () => {
  assert.equal(
    getRiderHistoryAccessibilityLabel({ rideId: "", pickup: "", dropoff: "", passengers: null, serviceClass: "", status: "", requestedAt: "bad-date" }),
    "Trip from Pickup unavailable to Drop-off unavailable. Status STATUS UNAVAILABLE. Unknown passengers. Service class UNKNOWN. Requested Date unavailable. Reference Unavailable."
  );
});

test("Rider agreement-version accessibility summary exposes immutable version, currency and both confirmations", () => {
  const confirmedAt = "2026-11-18T10:15:00.000Z";
  const label = getRiderAgreementVersionAccessibilityLabel({
    version: 3,
    amount: 425,
    currency: "inr",
    pickup: "Nizampet",
    dropoff: "Gachibowli",
    stops: ["Miyapur", "Kondapur"],
    createdAtUtc: "2026-11-18T10:00:00.000Z",
    proposedBy: "Driver",
    riderConfirmed: false,
    riderConfirmedAtUtc: null,
    driverConfirmed: true,
    driverConfirmedAtUtc: confirmedAt
  });
  assert.match(label, /^Agreement version 3\./);
  assert.match(label, /INR 425\.00/);
  assert.match(label, /Route: Nizampet to Gachibowli\. Stops: Miyapur, Kondapur\./);
  assert.ok(label.includes("Created " + new Date("2026-11-18T10:00:00.000Z").toLocaleString() + "."));
  assert.match(label, /Proposed by Driver/);
  assert.match(label, /Rider: Pending/);
  assert.ok(label.includes("Driver: Confirmed at " + new Date(confirmedAt).toLocaleString()));
});

test("Rider agreement version route and creation-time helpers expose the immutable snapshot", () => {
  const item = { pickup: "Nizampet", dropoff: "Gachibowli", stops: ["Miyapur", "Kondapur"], createdAtUtc: "2026-11-18T10:00:00.000Z" };
  assert.equal(getRiderAgreementVersionRouteLabel(item), "Route: Nizampet to Gachibowli. Stops: Miyapur, Kondapur.");
  assert.equal(getRiderAgreementVersionCreatedAtLabel(item), "Created " + new Date(item.createdAtUtc).toLocaleString() + ".");
});

test("Rider agreement version route and creation-time helpers fail safely for malformed snapshots", () => {
  const item = { pickup: " ", dropoff: "", stops: [null, 3, "  "], createdAtUtc: "not-a-date" };
  assert.equal(getRiderAgreementVersionRouteLabel(item), "Route: Pickup unavailable to Drop-off unavailable.");
  assert.equal(getRiderAgreementVersionCreatedAtLabel(item), "Creation time unavailable.");
});

test("Rider cancellation response validation requires the exact ride and a non-empty status", () => {
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Cancelled" }, "ride-123"), true);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Requested" }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "InProgress" }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Completed" }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Expired" }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-other", status: "Cancelled" }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "" }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "   " }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Cancelled " }, "ride-123"), false);
  assert.equal(isRiderCancellationResponseValid({ rideId: "ride-123", status: "Unknown" }, "ride-123"), false);
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
  assert.ok(source.includes("getRiderAgreementVersionRouteLabel(item)"));
  assert.ok(source.includes("getRiderAgreementVersionCreatedAtLabel(item)"));
  assert.ok(source.includes("accessibilityLabel={getRiderHistoryAccessibilityLabel(item)}"));
  assert.ok(source.includes("isRiderCancellationResponseValid(updated,rideId)"));
  const cancelStart = source.indexOf("const cancelRide");
  const cancelEnd = source.indexOf("const requestSelectedDriver", cancelStart);
  assert.ok(cancelStart >= 0 && cancelEnd > cancelStart);
  const cancelSource = source.slice(cancelStart, cancelEnd);
  assert.match(cancelSource, /await loadRideHistory\\(\\(\\)=>isCurrent\\(\\)\\)/);
});


test("Rider lifecycle labels are readable and expose only known lifecycle values", () => {
  const expected = {
    Requested: "REQUESTED",
    Matching: "FINDING DRIVER",
    Offered: "WAITING FOR DRIVER",
    Negotiating: "NEGOTIATING",
    AgreementConfirmed: "AGREEMENT CONFIRMED",
    Accepted: "DRIVER ACCEPTED",
    Arrived: "DRIVER ARRIVED",
    InProgress: "IN PROGRESS",
    Completed: "COMPLETED",
    Cancelled: "CANCELLED",
    Expired: "EXPIRED"
  };
  for (const [status, label] of Object.entries(expected)) {
    assert.equal(getRiderRideStatusLabel(status), label);
  }
  assert.equal(getRiderRideStatusLabel("FutureServerStatus"), "STATUS UNAVAILABLE");
  assert.equal(getRiderRideStatusLabel(""), "STATUS UNAVAILABLE");
  assert.equal(getRiderRideStatusLabel(null), "STATUS UNAVAILABLE");
});

test("Rider status events reject malformed identity/status but allow unknown future lifecycle values", () => {
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "InProgress" }, "ride-123"), true);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "FutureServerStatus" }, "ride-123"), true);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-other", status: "Completed" }, "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "" }, "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "  " }, "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "Completed " }, "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "x".repeat(65) }, "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "Completed\n" }, "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "Completed\u0085" }, "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid(null, "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid([], "ride-123"), false);
  assert.equal(isRiderRideStatusEventValid({ rideId: "ride-123", status: "Completed" }, " ride-123"), false);
});

test("Rider Activity uses safe labels and validates live status events before applying them", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.ok(source.includes("getRiderRideStatusLabel(ride.status)"));
  assert.ok(source.includes("getRiderRideStatusLabel(item.status)"));
  assert.ok(source.includes("accessibilityLabel={`Ride status ${getRiderRideStatusLabel(ride.status)}`}"));
  assert.ok(source.includes("isRiderRideStatusEventValid(update,ride.rideId)"));
});

test("Rider history accessibility hides unknown raw lifecycle values", () => {
  const label = getRiderHistoryAccessibilityLabel({
    rideId: "ride-unknown",
    pickup: "Pickup",
    dropoff: "Destination",
    passengers: 1,
    serviceClass: "cab",
    status: "FutureServerStatus",
    requestedAt: "2026-11-18T10:15:00.000Z"
  });
  assert.match(label, /Status STATUS UNAVAILABLE/);
  assert.doesNotMatch(label, /FutureServerStatus/);
});
