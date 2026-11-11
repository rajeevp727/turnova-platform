function applySessionStateReset(setters, values, role) {
  if (!setters || typeof setters !== "object") {
    throw new TypeError("Session state setters are required.");
  }

  const entries = Object.entries(values);
  for (const [key] of entries) {
    if (typeof setters[key] !== "function") {
      throw new TypeError(`Missing ${role} session state setter: ${key}`);
    }
  }

  for (const [key, value] of entries) setters[key](value);
}

function resetRiderSessionState(setters) {
  applySessionStateReset(setters, {
    tokens: null,
    user: null,
    mode: "login",
    email: "",
    password: "",
    name: "",
    tab: "Book",
    error: "",
    pickup: "Current location",
    dropoff: "HITEC City, Hyderabad",
    stopsText: "",
    passengers: "1",
    tripIntentText: "",
    tripIntentBusy: false,
    tripIntentNotice: "",
    helpQuestion: "",
    helpBusy: false,
    helpAnswer: "",
    ride: null,
    rideHistory: [],
    discovery: null,
    selectedDriverId: null,
    agreementHistory: [],
    agreementAmount: "",
    followDriverLocation: true,
    readyRideMapId: null
  }, "Rider");
}

function resetDriverSessionState(setters) {
  applySessionStateReset(setters, {
    tokens: null,
    user: null,
    mode: "login",
    email: "",
    password: "",
    name: "",
    tab: "Home",
    error: "",
    queue: [],
    membership: { isQueued: false, position: null },
    availabilityStatus: "Offline",
    loading: false,
    notice: "",
    helpQuestion: "",
    helpBusy: false,
    helpAnswer: "",
    availableRides: [],
    activeRide: null,
    driverRideHistory: [],
    agreementHistory: [],
    agreementAmount: "",
    paymentReportAmounts: {},
    onboardingProfile: null,
    unionAssignment: null,
    driverName: "",
    phoneNumber: "",
    vehicleMake: "",
    vehicleModel: "",
    vehicleRegistration: "",
    licenceReference: "",
    registrationReference: "",
    profilePhotoBase64: "",
    legacyServiceClass: null
  }, "Driver");
}

function finishLoadingIfSessionCurrent(setLoading, isSessionCurrent) {
  if (typeof setLoading !== "function") {
    throw new TypeError("Loading state setter is required.");
  }
  if (typeof isSessionCurrent !== "function") {
    throw new TypeError("Session-current predicate is required.");
  }
  if (!isSessionCurrent()) return false;
  setLoading(false);
  return true;
}

module.exports = { resetRiderSessionState, resetDriverSessionState, finishLoadingIfSessionCurrent };
