"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  normalizeRiderDiscoveryResponse,
  getVisibleUnionDrivers,
  getRiderDriverOptionProps,
  getRiderTripActionProps,
  getRiderTripFieldProps,
  canDiscoverRiderTrip,
  shouldApplyRiderDiscoveryResponse
} = require("./riderTripForm.js");

test("RIDER-DISCOVERY-PREFLIGHT-001: require pickup, destination and whole passenger count from 1 to 6", () => {
  assert.equal(canDiscoverRiderTrip("Nizampet", "HITEC City", 1), true);
  assert.equal(canDiscoverRiderTrip(" Nizampet ", " HITEC City ", 6), true);
  for (const [pickup, dropoff, passengers] of [
    ["", "HITEC City", 1],
    ["Nizampet", "", 1],
    ["  ", "HITEC City", 1],
    ["Nizampet", "  ", 1],
    [null, "HITEC City", 1],
    ["Nizampet", {}, 1],
    ["Nizampet", "HITEC City", null],
    ["Nizampet", "HITEC City", 0],
    ["Nizampet", "HITEC City", 7],
    ["Nizampet", "HITEC City", 1.5],
    ["Nizampet", "HITEC City", Number.NaN]
  ]) {
    assert.equal(canDiscoverRiderTrip(pickup, dropoff, passengers), false);
  }
});

test("RIDER-DISCOVERY-SEQUENCE-001: apply only the latest safe discovery request sequence", () => {
  assert.equal(shouldApplyRiderDiscoveryResponse(1, 1), true);
  assert.equal(shouldApplyRiderDiscoveryResponse(2, 2), true);
  assert.equal(shouldApplyRiderDiscoveryResponse(1, 2), false);
  assert.equal(shouldApplyRiderDiscoveryResponse(0, 0), false);
  assert.equal(shouldApplyRiderDiscoveryResponse(-1, -1), false);
  assert.equal(shouldApplyRiderDiscoveryResponse(Number.NaN, Number.NaN), false);
  assert.equal(shouldApplyRiderDiscoveryResponse(Number.MAX_SAFE_INTEGER + 1, Number.MAX_SAFE_INTEGER + 1), false);
});

test("RIDER-DISCOVERY-SESSION-001: fence discovery UI state by session and request sequence", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /const riderDiscoveryRequestSequence=useRef\(0\)/);
  assert.match(source, /const canDiscoverTrip=canDiscoverRiderTrip\(pickup,dropoff,parsePassengerCountInput\(passengers\)\)/);
  assert.match(source, /getRiderTripActionProps\("discover",loading\|\|!canDiscoverTrip,loading\)/);
  assert.match(source, /disabled=\{loading\|\|!canDiscoverTrip\}/);

  const resetStart = source.indexOf("const clearRiderSessionState");
  const resetEnd = source.indexOf("useEffect", resetStart);
  assert.ok(resetStart >= 0 && resetEnd > resetStart);
  const resetSource = source.slice(resetStart, resetEnd);
  assert.match(resetSource, /riderDiscoveryRequestSequence\.current \+= 1/);
  assert.match(resetSource, /setLoading\(false\)/);

  const searchStart = source.indexOf("const findNearbyVehicles=async()=>{");
  const searchEnd = source.indexOf("const cancelRide=async()=>{", searchStart);
  assert.ok(searchStart >= 0 && searchEnd > searchStart);
  const searchSource = source.slice(searchStart, searchEnd);
  assert.match(searchSource, /const requestEpoch=sessionEpoch\.current/);
  assert.match(searchSource, /const requestSequence=\+\+riderDiscoveryRequestSequence\.current/);
  assert.match(searchSource, /sessionEpoch\.current===requestEpoch/);
  assert.match(searchSource, /isAuthenticatedSessionCurrent\(sessionEpoch,authenticatedSessionEpoch\)/);
  assert.match(searchSource, /shouldApplyRiderDiscoveryResponse\(requestSequence,riderDiscoveryRequestSequence\.current\)/);
  assert.match(searchSource, /canDiscoverRiderTrip\(pickup,dropoff,passengerCount\)/);
  assert.match(searchSource, /if\(!isCurrent\(\)\)return/);
  assert.match(searchSource, /if\(isCurrent\(\)\)setLoading\(false\)/);
});

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

test("RIDER-DISCOVERY-RESPONSE-001: project only validated public Cab discovery fields", () => {
  const raw = {
    pickupZoneId: "zone-hyd-1",
    journeyDistanceKm: null,
    privateRootField: "must not survive",
    unions: [{
      unionId: "union-1",
      unionName: "Green Cab Union",
      privateUnionField: "must not survive",
      drivers: [{
        driverId: "driver-1",
        fullName: "Asha Rao",
        vehicleMake: "Maruti",
        vehicleModel: "Dzire",
        serviceClass: "cab",
        passengerCapacity: 4,
        distanceToPickupKm: 2.4,
        profilePhotoBase64: null,
        profilePhotoContentType: null,
        phoneNumber: "+910000000000",
        latitude: 17.4,
        longitude: 78.4,
        profilePhotoApprovalStatus: "Pending"
      }]
    }]
  };
  const result = normalizeRiderDiscoveryResponse(raw);
  assert.deepEqual(result, {
    pickupZoneId: "zone-hyd-1",
    journeyDistanceKm: null,
    unions: [{
      unionId: "union-1",
      unionName: "Green Cab Union",
      drivers: [{
        driverId: "driver-1",
        fullName: "Asha Rao",
        vehicleMake: "Maruti",
        vehicleModel: "Dzire",
        serviceClass: "cab",
        passengerCapacity: 4,
        distanceToPickupKm: 2.4,
        profilePhotoBase64: null,
        profilePhotoContentType: null
      }]
    }]
  });
  assert.equal("phoneNumber" in result.unions[0].drivers[0], false);
  assert.equal("latitude" in result.unions[0].drivers[0], false);
  assert.equal("privateRootField" in result, false);
});

test("RIDER-DISCOVERY-RESPONSE-002: reject malformed contracts, incomplete vehicle details and photo metadata", () => {
  const validDriver = {
    driverId: "driver-1", fullName: "Asha Rao", vehicleMake: "Maruti", vehicleModel: "Dzire",
    serviceClass: "cab", passengerCapacity: 4, distanceToPickupKm: 2.4,
    profilePhotoBase64: null, profilePhotoContentType: null
  };
  const validUnion = { unionId: "union-1", unionName: "Green Cab Union", drivers: [validDriver] };
  const valid = { pickupZoneId: "zone-hyd-1", journeyDistanceKm: null, unions: [validUnion] };
  for (const invalid of [
    null, [], {}, { ...valid, pickupZoneId: " " }, { ...valid, journeyDistanceKm: -1 },
    { ...valid, journeyDistanceKm: Number.NaN }, { ...valid, unions: {} },
    { ...valid, unions: [{ ...validUnion, drivers: null }] },
    { ...valid, unions: [validUnion, validUnion] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, driverId: " " }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, vehicleMake: " " }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, vehicleModel: "" }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, vehicleMake: "x".repeat(61) }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, vehicleModel: "x".repeat(61) }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, serviceClass: "bike" }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, distanceToPickupKm: Infinity }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, passengerCapacity: 7 }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, profilePhotoBase64: "aGVsbG8=" }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, profilePhotoBase64: "aGVsbG8=", profilePhotoContentType: "text/html" }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, profilePhotoBase64: "aGVsbG8=", profilePhotoContentType: "image/jpeg" }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, profilePhotoBase64: "/9j/AA==", profilePhotoContentType: "image/jpeg", profilePhotoApprovalStatus: "Pending" }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [{ ...validDriver, profilePhotoBase64: "/9j/AA==", profilePhotoContentType: "image/jpeg", profilePhotoApprovalStatus: "Approved", profilePhotoApprovedByUnionId: "other-union" }] }] },
    { ...valid, unions: [{ ...validUnion, drivers: [validDriver, validDriver] }] }
  ]) {
    assert.equal(normalizeRiderDiscoveryResponse(invalid), null);
  }
});

test("RIDER-DISCOVERY-RESPONSE-003: default JPEG content type only for a server-filtered approved photo", () => {
  const driver = {
    driverId: "driver-photo", fullName: "Asha Rao", vehicleMake: "Maruti", vehicleModel: "Dzire",
    serviceClass: "cab", passengerCapacity: 4, distanceToPickupKm: 1.2,
    profilePhotoBase64: "/9j/AA==", profilePhotoContentType: null
  };
  const result = normalizeRiderDiscoveryResponse({
    pickupZoneId: "zone-hyd-1", journeyDistanceKm: null,
    unions: [{ unionId: "union-1", unionName: "Green Cab Union", drivers: [driver] }]
  });
  assert.equal(result.unions[0].drivers[0].profilePhotoContentType, "image/jpeg");
});

test("Rider union display keeps at most the first three server-ordered drivers", () => {
  const drivers = [{ driverId: "d1" }, { driverId: "d2" }, { driverId: "d3" }, { driverId: "d4" }];
  assert.deepEqual(getVisibleUnionDrivers(drivers), drivers.slice(0, 3));
  assert.deepEqual(getVisibleUnionDrivers([drivers[2], drivers[0]]), [drivers[2], drivers[0]]);
  assert.deepEqual(getVisibleUnionDrivers(null), []);
  assert.deepEqual(getVisibleUnionDrivers({}), []);
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

test("Rider destination starts empty so discovery cannot use an assumed demo destination", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.ok(source.includes('const [dropoff,setDropoff]=useState("");'));
  assert.ok(!source.includes('useState("HITEC City, Hyderabad")'));
  assert.match(source, /canDiscoverRiderTrip\(pickup,dropoff,parsePassengerCountInput\(passengers\)\)/);
  assert.match(source, /disabled=\{loading\|\|!canDiscoverTrip\}/);
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
  assert.match(source, /getVisibleUnionDrivers\(union\.drivers\)\.map/);
  assert.match(source, /normalizeRiderDiscoveryResponse\(rawResult\)/);
  assert.match(source, /The discovery response is invalid/);
  assert.match(source, /testID=\{`rider-driver-option-\$\{driver\.driverId\}`\}/);
  assert.match(source, /accessibilityLiveRegion="polite"/);
  assert.match(source, /accessibilityLiveRegion="assertive"/);
});
