function locationError(message, code) {
  const error = new Error(message);
  error.code = code;
  return error;
}

async function getForegroundPickupCoordinates(location) {
  if (
    !location ||
    typeof location.requestForegroundPermissionsAsync !== "function" ||
    typeof location.getCurrentPositionAsync !== "function"
  ) {
    throw new Error("Foreground location is unavailable.");
  }

  const permission = await location.requestForegroundPermissionsAsync();
  if (!permission?.granted) {
    throw locationError(
      "Location permission is required to find nearby Cab drivers.",
      "LOCATION_PERMISSION_REQUIRED"
    );
  }

  const accuracy = location.Accuracy?.Balanced;
  const position = await location.getCurrentPositionAsync(
    accuracy === undefined ? {} : { accuracy }
  );
  const latitude = position?.coords?.latitude;
  const longitude = position?.coords?.longitude;

  if (
    !Number.isFinite(latitude) ||
    latitude < -90 ||
    latitude > 90 ||
    !Number.isFinite(longitude) ||
    longitude < -180 ||
    longitude > 180
  ) {
    throw locationError(
      "Unable to determine a valid current pickup location.",
      "INVALID_PICKUP_COORDINATES"
    );
  }

  return { latitude, longitude };
}

async function discoverNearbyCabDrivers(location, request, passengers) {
  if (typeof request !== "function") {
    throw new Error("A discovery request function is required.");
  }

  const coordinates = await getForegroundPickupCoordinates(location);
  const query = [
    "latitude=" + encodeURIComponent(String(coordinates.latitude)),
    "longitude=" + encodeURIComponent(String(coordinates.longitude)),
    "passengers=" + encodeURIComponent(String(passengers))
  ].join("&");
  const discovery = await request("/api/v1/rider/discovery?" + query);

  return { coordinates, discovery };
}

module.exports = {
  getForegroundPickupCoordinates,
  discoverNearbyCabDrivers
};
