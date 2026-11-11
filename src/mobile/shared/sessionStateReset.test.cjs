const assert = require("node:assert/strict");
const test = require("node:test");
const fs = require("node:fs");
const path = require("node:path");
const { resetRiderSessionState, resetDriverSessionState, finishLoadingIfSessionCurrent } = require("./sessionStateReset.js");

function createSetters(keys) {
  const values = {};
  const setters = Object.fromEntries(keys.map(key => [key, value => { values[key] = value; }]));
  return { setters, values };
}

const riderKeys = [
  "tokens", "user", "mode", "email", "password", "name", "tab", "error", "pickup", "dropoff", "stopsText", "passengers",
  "tripIntentText", "tripIntentBusy", "tripIntentNotice", "helpQuestion", "helpBusy",
  "helpAnswer", "ride", "rideHistory", "discovery", "selectedDriverId",
  "agreementHistory", "agreementAmount", "followDriverLocation", "readyRideMapId"
];

const driverKeys = [
  "tokens", "user", "mode", "email", "password", "name", "tab", "error", "queue", "membership", "availabilityStatus", "loading", "notice",
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
    tokens: null, user: null, mode: "login", email: "", password: "", name: "", tab: "Book", error: "", pickup: "Current location",
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
    tokens: null, user: null, mode: "login", email: "", password: "", name: "", tab: "Home", error: "", queue: [],
    membership: { isQueued: false, position: null }, availabilityStatus: "Offline", loading: false,
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


test("Rider session reset invalidates cached pickup coordinates without releasing an in-flight booking guard", () => {
  const app = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const attemptStart = app.indexOf("const resetSelectedBookingAttempt");
  const resetStart = app.indexOf("const clearRiderSessionState", attemptStart);
  const resetEnd = app.indexOf("useEffect", resetStart);
  assert.notEqual(attemptStart, -1);
  assert.notEqual(resetStart, -1);
  assert.notEqual(resetEnd, -1);
  const attempt = app.slice(attemptStart, resetStart);
  const reset = app.slice(resetStart, resetEnd);
  assert.match(attempt, /selectedBookingLocation\.current\.reset\(\)/);
  assert.match(attempt, /bookingRequestKeyRef\.current\s*=\s*nextKey/);
  assert.match(reset, /resetSelectedBookingAttempt\(\)/);
  assert.doesNotMatch(reset, /bookingInFlight\.current\s*=\s*false/);
});

test("Rider and Driver session reset clears auth form secrets and successful login drops the password", () => {
  const rider = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const driver = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");
  const riderAuthStart = rider.indexOf("const authenticate=async()=>");
  const riderAuthEnd = rider.indexOf("const draftTripIntent", riderAuthStart);
  const driverAuthStart = driver.indexOf("const authenticate = async");
  const driverAuthEnd = driver.indexOf("const loadAvailability", driverAuthStart);
  assert.notEqual(riderAuthStart, -1);
  assert.notEqual(riderAuthEnd, -1);
  assert.notEqual(driverAuthStart, -1);
  assert.notEqual(driverAuthEnd, -1);
  const riderAuth = rider.slice(riderAuthStart, riderAuthEnd);
  const driverAuth = driver.slice(driverAuthStart, driverAuthEnd);
  assert.match(riderAuth, /clearRiderSessionState\(credentials\)/);
  assert.ok(rider.includes("setMode(preserveAuthInput.mode)"));
  assert.ok(driver.includes("setMode(preserveAuthInput.mode)"));
  assert.ok(riderAuth.includes('setPassword("")'));
  assert.match(driverAuth, /clearDriverSessionState\(credentials\)/);
  assert.ok(driverAuth.includes('setPassword("")'));
});


test("AUTH-LOGIN-062 clears Driver SignalR offer deduplication when resetting a session", () => {
  const app = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");
  const resetStart = app.indexOf("const clearDriverSessionState");
  const resetEnd = app.indexOf("const serviceClass", resetStart);
  assert.notEqual(resetStart, -1);
  assert.notEqual(resetEnd, -1);
  const reset = app.slice(resetStart, resetEnd);

  assert.match(reset, /receivedOfferIds\.current\.clear\(\);/);
  assert.match(app, /const receivedOfferIds = useRef<Set<string>>\(new Set\(\)\);/);
  assert.match(app, /if \(receivedOfferIds\.current\.size > 500\)/);
});


test("stale in-flight payslip finalizer cannot clear a newer authenticated session loading state", async () => {
  let sessionEpoch = 7;
  const requestEpoch = sessionEpoch;
  const isSessionCurrent = () => sessionEpoch === requestEpoch;
  let loading = true;
  let completeRequest;
  const request = new Promise(resolve => { completeRequest = resolve; });
  const finalized = request.finally(() => finishLoadingIfSessionCurrent(
    value => { loading = value; },
    isSessionCurrent
  ));

  // Account A's request remains in flight while Account B starts authenticating.
  sessionEpoch += 1;
  loading = true;
  completeRequest();
  await finalized;

  assert.equal(isSessionCurrent(), false);
  assert.equal(loading, true);
});

test("current payslip finalizer clears loading for its own authenticated session", () => {
  let loading = true;
  const cleared = finishLoadingIfSessionCurrent(value => { loading = value; }, () => true);

  assert.equal(cleared, true);
  assert.equal(loading, false);
});

test("Driver payslip finalizer uses the captured session guard and session reset clears loading", () => {
  const app = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");
  const resetStart = app.indexOf("const clearDriverSessionState");
  const resetEnd = app.indexOf("const serviceClass", resetStart);
  const downloadStart = app.indexOf("const downloadPayslip = async");
  const downloadEnd = app.indexOf("const loadOnboarding", downloadStart);
  assert.notEqual(resetStart, -1);
  assert.notEqual(resetEnd, -1);
  assert.notEqual(downloadStart, -1);
  assert.notEqual(downloadEnd, -1);
  const reset = app.slice(resetStart, resetEnd);
  const download = app.slice(downloadStart, downloadEnd);
  const finallyStart = download.lastIndexOf("} finally {");
  assert.notEqual(finallyStart, -1);
  const finalizer = download.slice(finallyStart);

  assert.match(reset, /loading:\s*setLoading/);
  assert.match(download, /const isSessionCurrent = \(\) => sessionEpoch\.current === requestEpoch/);
  assert.match(finalizer, /finishLoadingIfSessionCurrent\(setLoading, isSessionCurrent\)/);
  assert.doesNotMatch(finalizer, /setLoading\(false\)/);
});


test("stale logout cleanup finalizer cannot clear a newer session loading state", () => {
  let sessionEpoch = 11;
  const logoutEpoch = sessionEpoch;
  let loading = true;

  sessionEpoch += 1;
  loading = true;
  const cleared = finishLoadingIfSessionCurrent(value => { loading = value; }, () => sessionEpoch === logoutEpoch);

  assert.equal(cleared, false);
  assert.equal(loading, true);
});

test("Driver logout storage cleanup failure releases loading only for the same logout epoch", () => {
  const app = fs.readFileSync(path.join(__dirname, "../driver-app/App.tsx"), "utf8");
  const logoutStart = app.indexOf("const logout = async");
  const logoutEnd = app.indexOf("const askHelp", logoutStart);
  assert.notEqual(logoutStart, -1);
  assert.notEqual(logoutEnd, -1);
  const logout = app.slice(logoutStart, logoutEnd);
  const catchStart = logout.indexOf("} catch {");
  const returnEnd = logout.indexOf("return;", catchStart);
  assert.notEqual(catchStart, -1);
  assert.notEqual(returnEnd, -1);
  const catchBlock = logout.slice(catchStart, returnEnd);

  assert.match(catchBlock, /finishLoadingIfSessionCurrent\(setLoading, \(\) => sessionEpoch\.current === logoutEpoch\)/);
  assert.match(catchBlock, /if \(sessionEpoch\.current === logoutEpoch\) setError/);
});
