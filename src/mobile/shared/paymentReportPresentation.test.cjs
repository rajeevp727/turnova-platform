const assert = require("node:assert/strict");
const test = require("node:test");
const { paymentReportPresentation } = require("./paymentReportPresentation.js");

test("payment report labels use the current confirmed agreement currency", () => {
  assert.deepEqual(
    paymentReportPresentation({ version: 1, amount: 500, currency: "USD" }),
    {
      agreedAmountLabel: "Agreed USD 500.00",
      amountReceivedPlaceholder: "Amount received (USD)"
    }
  );
  assert.deepEqual(
    paymentReportPresentation({ version: 2, amount: 400.5, currency: "inr" }),
    {
      agreedAmountLabel: "Agreed INR 400.50",
      amountReceivedPlaceholder: "Amount received (INR)"
    }
  );
});

test("missing or malformed agreement summaries never assume INR", () => {
  for (const agreement of [
    null,
    undefined,
    { amount: 500 },
    { amount: 500, currency: "US" },
    { amount: Number.NaN, currency: "USD" },
    { amount: -1, currency: "USD" }
  ]) {
    assert.deepEqual(paymentReportPresentation(agreement), {
      agreedAmountLabel: null,
      amountReceivedPlaceholder: "Amount received"
    });
  }
});
