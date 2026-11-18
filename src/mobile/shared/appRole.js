const APP_ROLES = new Set(["Rider", "Driver"]);

function requireAppRole(user, expectedRole) {
  if (!APP_ROLES.has(expectedRole)) {
    throw new Error("Unsupported TurnOva app role.");
  }

  if (!user || typeof user !== "object" || user.role !== expectedRole) {
    throw new Error("This account is not authorized for this app.");
  }

  return user;
}

module.exports = { requireAppRole };
