const { getForegroundPickupCoordinates } = require("./foregroundPickupLocation.js");

function createSelectedBookingLocation(location) {
  let generation = 0;
  let coordinates = null;
  let pending = null;
  let pendingGeneration = -1;

  return {
    capture: async () => {
      const requestGeneration = generation;
      if (coordinates) return coordinates;

      if (!pending || pendingGeneration !== requestGeneration) {
        pendingGeneration = requestGeneration;
        const request = getForegroundPickupCoordinates(location)
          .then(currentCoordinates => {
            if (generation !== requestGeneration) {
              throw new Error("Selected booking changed while refreshing pickup location.");
            }
            coordinates = currentCoordinates;
            return currentCoordinates;
          })
          .finally(() => {
            if (generation === requestGeneration && pendingGeneration === requestGeneration) {
              pending = null;
              pendingGeneration = -1;
            }
          });
        pending = request;
      }

      const currentCoordinates = await pending;
      if (generation !== requestGeneration) {
        throw new Error("Selected booking changed while refreshing pickup location.");
      }
      return currentCoordinates;
    },
    reset: () => {
      generation += 1;
      coordinates = null;
      pending = null;
      pendingGeneration = -1;
    }
  };
}

module.exports = { createSelectedBookingLocation };
