function formatAgreementAmount(amount, currency) {
  const normalizedCurrency = typeof currency === "string" ? currency.trim().toUpperCase() : "";
  const currencyLabel = /^[A-Z]{3}$/.test(normalizedCurrency)
    ? normalizedCurrency
    : "Currency unavailable";
  const amountLabel = typeof amount === "number" && Number.isFinite(amount)
    ? amount.toFixed(2)
    : "—";

  return `${currencyLabel} ${amountLabel}`;
}

module.exports = { formatAgreementAmount };
