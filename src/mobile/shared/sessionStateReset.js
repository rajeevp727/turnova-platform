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
    tab: "Book",
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
    tab: "Home",
    queue: [],
    membership: { isQueued: false, position: null },
    availabilityStatus: "Offline",
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

module.exports = { resetRiderSessionState, resetDriverSessionState };
