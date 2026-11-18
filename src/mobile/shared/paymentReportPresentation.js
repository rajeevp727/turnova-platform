function paymentReportPresentation(currentAgreement) {
  const currencyValue = currentAgreement?.currency;
  const amount = currentAgreement?.amount;
  if (
    typeof currencyValue !== "string" ||
    !/^[A-Za-z]{3}$/.test(currencyValue.trim()) ||
    typeof amount !== "number" ||
    !Number.isFinite(amount) ||
    amount < 0
  ) {
    return {
      agreedAmountLabel: null,
      amountReceivedPlaceholder: "Amount received"
    };
  }

  const currency = currencyValue.trim().toUpperCase();
  return {
    agreedAmountLabel: `Agreed ${currency} ${amount.toFixed(2)}`,
    amountReceivedPlaceholder: `Amount received (${currency})`
  };
}

module.exports = { paymentReportPresentation };
