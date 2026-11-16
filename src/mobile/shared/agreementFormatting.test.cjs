const assert = require("node:assert/strict");
const test = require("node:test");
const { formatAgreementAmount } = require("./agreementFormatting.js");

test("agreement history displays the persisted INR currency code", () => {
  assert.equal(formatAgreementAmount(125.5, "INR"), "INR 125.50");
});

test("agreement history displays non-INR persisted currency codes without a rupee symbol", () => {
  assert.equal(formatAgreementAmount(82, "USD"), "USD 82.00");
  assert.equal(formatAgreementAmount(82, "EUR"), "EUR 82.00");
});

test("currency codes are trimmed and normalized without changing the amount", () => {
  assert.equal(formatAgreementAmount(10.25, " usd "), "USD 10.25");
});

test("missing or malformed currency codes never silently default to INR", () => {
  assert.equal(formatAgreementAmount(10, null), "Currency unavailable 10.00");
  assert.equal(formatAgreementAmount(10, ""), "Currency unavailable 10.00");
  assert.equal(formatAgreementAmount(10, "INR₹"), "Currency unavailable 10.00");
});

test("invalid amounts render safely without throwing", () => {
  assert.equal(formatAgreementAmount(Number.NaN, "USD"), "USD —");
  assert.equal(formatAgreementAmount(Number.POSITIVE_INFINITY, "USD"), "USD —");
  assert.equal(formatAgreementAmount("10", "USD"), "USD —");
});
