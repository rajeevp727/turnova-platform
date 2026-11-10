const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const { resetRiderSessionState, resetDriverSessionState } = require("./sessionStateReset.js");

function createSetters(keys) {
  const values = {};
  const setters = Object.fromEntries(keys.map(key => [key, value => { values[key] = value; }]));
  return { setters, values };
}

const riderKeys = [
  "tokens", "user", "tab", "pickup", "dropoff", "stopsText", "passengers",
  "tripIntentText", "tripIntentBusy", "tripIntentNotice", "helpQuestion", "helpBusy",
  "helpAnswer", "ride", "rideHistory", "discovery", "selectedDriverId",
  "agreementHistory", "agreementAmount", "followDriverLocation", "readyRideMapId"
];

const driverKeys = [
  "tokens", "user", "tab", "queue", "membership", "availabilityStatus", "notice",
  "helpQuestion", "helpBusy", "helpAnswer", "availableRides", "activeRide",
  "driverRideHistory", "agreementHistory", "agreementAmount", "paymentReportAmounts",
  "onboardingProfile", "unionAssignment", "driverName", "phoneNumber", "vehicleMake",
  "vehicleModel", "vehicleRegistration", "licenceReference", "registrationReference",
  "profilePhotoBase64", "legacyServiceClass"
];

test("Rider session reset clears prior-account trip, discovery, agreement, route and help state", () => {
  const { setters, values } = createSetters(riderKeys);
  resetRiderSessionState(setters);

  assert.deepEqual(values, {
    tokens: null, user: null, tab: "Book", pickup: "Current location",
    dropoff: "HITEC City, Hyderabad", stopsText: "", passengers: "1",
    tripIntentText: "", tripIntentBusy: false, tripIntentNotice: "",
    helpQuestion: "", helpBusy: false, helpAnswer: "", ride: null, rideHistory: [],
    discovery: null, selectedDriverId: null, agreementHistory: [], agreementAmount: "",
    followDriverLocation: true, readyRideMapId: null
  });
});

test("Driver session reset clears prior-account queue, ride, payment, onboarding and Union state", () => {
  const { setters, values } = createSetters(driverKeys);
  resetDriverSessionState(setters);

  assert.deepEqual(values, {
    tokens: null, user: null, tab: "Home", queue: [],
    membership: { isQueued: false, position: null }, availabilityStatus: "Offline",
    notice: "", helpQuestion: "", helpBusy: false, helpAnswer: "",
    availableRides: [], activeRide: null, driverRideHistory: [], agreementHistory: [],
    agreementAmount: "", paymentReportAmounts: {}, onboardingProfile: null,
    unionAssignment: null, driverName: "", phoneNumber: "", vehicleMake: "",
    vehicleModel: "", vehicleRegistration: "", licenceReference: "",
    registrationReference: "", profilePhotoBase64: "", legacyServiceClass: null
  });
});

test("session reset validates every setter before mutating any state", () => {
  const calls = [];
  assert.throws(
    () => resetRiderSessionState({ tokens: value => calls.push(value) }),
    /Missing Rider session state setter/
  );
  assert.deepEqual(calls, []);
});

test("each reset receives fresh mutable collections", () => {
  const riderFirst = createSetters(riderKeys);
  const riderSecond = createSetters(riderKeys);
  resetRiderSessionState(riderFirst.setters);
  resetRiderSessionState(riderSecond.setters);
  assert.notEqual(riderFirst.values.rideHistory, riderSecond.values.rideHistory);
  assert.notEqual(riderFirst.values.agreementHistory, riderSecond.values.agreementHistory);

  const driverFirst = createSetters(driverKeys);
  const driverSecond = createSetters(driverKeys);
  resetDriverSessionState(driverFirst.setters);
  resetDriverSessionState(driverSecond.setters);
  assert.notEqual(driverFirst.values.queue, driverSecond.values.queue);
  assert.notEqual(driverFirst.values.paymentReportAmounts, driverSecond.values.paymentReportAmounts);
});


test("Driver payslip refresh expiry delegates to the complete session reset", () => {
  const app = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");
  const downloadStart = app.indexOf("const downloadPayslip = async");
  assert.notEqual(downloadStart, -1);
  const expiryStart = app.indexOf("if (response.status === 401)", downloadStart);
  assert.notEqual(expiryStart, -1);
  const expiryEnd = app.indexOf("if (!response.ok)", expiryStart);
  assert.notEqual(expiryEnd, -1);
  const expiryBranch = app.slice(expiryStart, expiryEnd);

  assert.match(expiryBranch, /clearDriverSessionState\(\);/);
  assert.doesNotMatch(
    expiryBranch,
    /setTokens\(null\)|setUser\(null\)|setActiveRide\(null\)|setDriverRideHistory\(\[\]\)/
  );
});
