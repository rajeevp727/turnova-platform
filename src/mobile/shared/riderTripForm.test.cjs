"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  getRiderDriverOptionProps,
  getRiderTripActionProps,
  getRiderTripFieldProps
} = require("./riderTripForm.js");

test("Rider trip draft input explains that it is optional and never books or charges", () => {
  const props = getRiderTripFieldProps("tripIntent", false);
  assert.equal(props.accessibilityLabel, "Describe your trip");
  assert.match(props.accessibilityHint, /editable draft only/);
  assert.deepEqual(props.accessibilityState, { disabled: false });
});

test("Rider pickup and destination labels distinguish landmark text from GPS and routing", () => {
  const pickup = getRiderTripFieldProps("pickup", false);
  const dropoff = getRiderTripFieldProps("dropoff", false);
  assert.equal(pickup.accessibilityLabel, "Pickup landmark or note");
  assert.match(pickup.accessibilityHint, /Device GPS is used for pickup-zone matching/);
  assert.equal(dropoff.accessibilityLabel, "Destination landmark or note");
  assert.match(dropoff.accessibilityHint, /not used to estimate the journey distance/);
});

test("Rider intermediate stops and passenger count provide ordered and bounded instructions", () => {
  const stops = getRiderTripFieldProps("stops", true);
  const passengers = getRiderTripFieldProps("passengers", false);
  assert.match(stops.accessibilityHint, /one stop per line in the order/);
  assert.deepEqual(stops.accessibilityState, { disabled: true });
  assert.equal(passengers.accessibilityLabel, "Passenger count");
  assert.match(passengers.accessibilityHint, /whole number from 1 to 6/);
});

test("Rider discovery actions expose disabled and busy states without automatic assignment", () => {
  const idle = getRiderTripActionProps("discover", false, false);
  assert.equal(idle.accessibilityRole, "button");
  assert.equal(idle.accessibilityLabel, "Find nearby Cab drivers");
  assert.match(idle.accessibilityHint, /You choose a driver; no automatic assignment/);
  assert.deepEqual(idle.accessibilityState, { disabled: false, busy: false });

  const busy = getRiderTripActionProps("discover", true, true);
  assert.equal(busy.accessibilityLabel, "Searching nearby Cab drivers");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
});

test("Rider trip draft and selected-driver request actions preserve direct negotiation and no-payment scope", () => {
  const draft = getRiderTripActionProps("draft", true, false);
  const request = getRiderTripActionProps("requestDriver", false, false);
  assert.match(draft.accessibilityHint, /does not book or charge/);
  assert.equal(request.accessibilityLabel, "Request this driver");
  assert.match(request.accessibilityHint, /direct fare negotiation/);
  assert.match(request.accessibilityHint, /does not quote or collect payment/);
});

test("Rider driver option exposes radio selection, union, Cab details and pickup-only proximity", () => {
  const driver = {
    fullName: "Asha Rao",
    vehicleMake: "Maruti",
    vehicleModel: "Dzire",
    passengerCapacity: 4,
    distanceToPickupKm: 2.34
  };
  const selected = getRiderDriverOptionProps(driver, true, false, "Green Cab Union");
  assert.equal(selected.accessibilityRole, "radio");
  assert.match(selected.accessibilityLabel, /Asha Rao, Maruti Dzire, Cab, Green Cab Union union/);
  assert.match(selected.accessibilityLabel, /2.3 kilometres from pickup/);
  assert.match(selected.accessibilityHint, /not journey distance/);
  assert.deepEqual(selected.accessibilityState, { checked: true, disabled: false });

  const disabled = getRiderDriverOptionProps(driver, false, true);
  assert.deepEqual(disabled.accessibilityState, { checked: false, disabled: true });
});

test("Rider trip helpers reject unknown fields and actions", () => {
  assert.throws(() => getRiderTripFieldProps("phone", false), /Unknown Rider trip field/);
  assert.throws(() => getRiderTripActionProps("pay", false, false), /Unknown Rider trip action/);
});

test("Rider booking screen wires accessible trip fields, actions and explicit driver selection", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  for (const id of [
    "rider-trip-intent",
    "rider-trip-intent-draft",
    "rider-trip-intent-notice",
    "rider-pickup",
    "rider-dropoff",
    "rider-stops",
    "rider-passengers",
    "rider-passengers-error",
    "rider-discover-drivers",
    "rider-request-selected-driver"
  ]) {
    assert.ok(source.includes('testID="' + id + '"'), "Missing Rider booking testID: " + id);
  }
  assert.match(source, /getRiderTripFieldProps\("tripIntent",loading\)/);
  assert.match(source, /getRiderTripFieldProps\("pickup",loading\)/);
  assert.match(source, /getRiderTripFieldProps\("dropoff",loading\)/);
  assert.match(source, /getRiderTripFieldProps\("stops",loading\)/);
  assert.match(source, /getRiderTripFieldProps\("passengers",loading\)/);
  assert.match(source, /getRiderTripActionProps\("draft"/);
  assert.match(source, /getRiderTripActionProps\("discover"/);
  assert.match(source, /getRiderTripActionProps\("requestDriver"/);
  assert.match(source, /getRiderDriverOptionProps\(driver,selected,loading,union\.unionName\)/);
  assert.match(source, /testID=\{`rider-driver-option-\$\{driver\.driverId\}`\}/);
  assert.match(source, /accessibilityLiveRegion="polite"/);
  assert.match(source, /accessibilityLiveRegion="assertive"/);
});
