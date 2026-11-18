const assert = require("node:assert/strict");
const test = require("node:test");
const { requireAppRole } = require("./appRole.js");

test("accepts matching Rider and Driver app profiles", () => {
  const rider = { id: "rider-1", role: "Rider" };
  const driver = { id: "driver-1", role: "Driver" };

  assert.equal(requireAppRole(rider, "Rider"), rider);
  assert.equal(requireAppRole(driver, "Driver"), driver);
});

test("rejects a Driver profile in Rider and a Rider profile in Driver", () => {
  assert.throws(
    () => requireAppRole({ id: "driver-1", role: "Driver" }, "Rider"),
    /not authorized for this app/
  );
  assert.throws(
    () => requireAppRole({ id: "rider-1", role: "Rider" }, "Driver"),
    /not authorized for this app/
  );
});

test("rejects missing profiles and missing roles", () => {
  assert.throws(() => requireAppRole(null, "Rider"), /not authorized for this app/);
  assert.throws(() => requireAppRole({ id: "rider-1" }, "Rider"), /not authorized for this app/);
});

test("rejects unsupported expected app roles", () => {
  assert.throws(
    () => requireAppRole({ id: "admin-1", role: "Admin" }, "Admin"),
    /Unsupported TurnOva app role/
  );
});
