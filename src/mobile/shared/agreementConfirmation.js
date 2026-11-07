function agreementConfirmationLabel(party, confirmed, confirmedAtUtc) {
  if (party !== "Rider" && party !== "Driver") {
    throw new TypeError("Agreement party must be Rider or Driver.");
  }

  if (!confirmed) return party + ": Pending";
  if (typeof confirmedAtUtc !== "string" || confirmedAtUtc.trim().length === 0) {
    return party + ": Confirmed";
  }

  const confirmedAt = new Date(confirmedAtUtc);
  if (!Number.isFinite(confirmedAt.getTime())) return party + ": Confirmed";

  return party + ": Confirmed at " + confirmedAt.toLocaleString();
}

module.exports = { agreementConfirmationLabel };
