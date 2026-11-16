function canLoadDriverRideHistory({ userId, hasAccessToken, isAuthenticated } = {}) {
  return typeof userId === "string" &&
    userId.trim().length > 0 &&
    hasAccessToken === true &&
    isAuthenticated === true;
}

function canMaintainDriverRideSession({ userId, rideId, hasAccessToken, isAuthenticated } = {}) {
  return canLoadDriverRideHistory({ userId, hasAccessToken, isAuthenticated }) &&
    typeof rideId === "string" &&
    rideId.trim().length > 0;
}

module.exports = {
  canLoadDriverRideHistory,
  canMaintainDriverRideSession
};
