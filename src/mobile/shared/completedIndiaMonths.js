const INDIA_TIME_ZONE = "Asia/Kolkata";
const MAX_COMPLETED_MONTHS = 12;

function getIndiaYearMonth(now) {
  if (!(now instanceof Date) || !Number.isFinite(now.getTime())) {
    throw new TypeError("A valid Date is required.");
  }

  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: INDIA_TIME_ZONE,
    year: "numeric",
    month: "2-digit"
  }).formatToParts(now);
  const year = Number(parts.find(part => part.type === "year")?.value);
  const month = Number(parts.find(part => part.type === "month")?.value);
  if (!Number.isInteger(year) || !Number.isInteger(month) || month < 1 || month > 12) {
    throw new RangeError("The India-local year and month could not be determined.");
  }
  return { year, month };
}

function getCompletedIndiaMonths(now = new Date(), count = MAX_COMPLETED_MONTHS) {
  if (!Number.isInteger(count) || count < 1 || count > MAX_COMPLETED_MONTHS) {
    throw new RangeError(`count must be an integer between 1 and ${MAX_COMPLETED_MONTHS}.`);
  }

  const { year, month } = getIndiaYearMonth(now);
  const currentMonthIndex = year * 12 + month - 1;

  return Array.from({ length: count }, (_, offset) => {
    const index = currentMonthIndex - offset - 1;
    return {
      year: Math.floor(index / 12),
      month: ((index % 12) + 12) % 12 + 1
    };
  });
}

function formatIndiaMonthLabel(year, month, locale) {
  if (!Number.isInteger(year) || year < 2020 || year > 9999 || !Number.isInteger(month) || month < 1 || month > 12) {
    throw new RangeError("A valid year and month are required.");
  }

  const representativeDate = new Date(Date.UTC(year, month - 1, 15, 12, 0, 0));
  return new Intl.DateTimeFormat(locale, {
    timeZone: INDIA_TIME_ZONE,
    month: "long",
    year: "numeric"
  }).format(representativeDate);
}

module.exports = { getCompletedIndiaMonths, formatIndiaMonthLabel };
