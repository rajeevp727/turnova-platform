"use strict";

const ISO_TIMESTAMP_WITH_OFFSET = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?(Z|([+-])(\d{2}):(\d{2}))$/i;

/**
 * Validate an offset-qualified ISO-8601 timestamp without Date.parse's
 * calendar-day normalization (for example, February 30 becoming March 2).
 * @param {unknown} value
 * @returns {value is string}
 */
function isStrictIsoTimestampWithOffset(value) {
  if (typeof value !== "string") return false;
  const match = ISO_TIMESTAMP_WITH_OFFSET.exec(value);
  if (!match) return false;

  const [year, month, day, hour, minute, second] = match.slice(1, 7).map(Number);
  const fraction = match[7];
  if (year < 1 || year > 9999 ||
      (fraction !== undefined && fraction.length > 7) ||
      month < 1 || month > 12 || day < 1 ||
      hour > 23 || minute > 59 || second > 59) return false;

  const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const daysInMonth = [31, leapYear ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  if (day > daysInMonth[month - 1]) return false;

  if (match[10] !== undefined) {
    const offsetHours = Number(match[10]);
    const offsetMinutes = Number(match[11]);
    // .NET DateTimeOffset permits offsets only through exactly +/-14:00.
    if (offsetHours > 14 || offsetMinutes > 59 ||
        (offsetHours === 14 && offsetMinutes !== 0)) return false;
  }

  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) return false;

  // DateTimeOffset's UTC instant must remain in the .NET DateTime range too.
  const utcYear = new Date(timestamp).getUTCFullYear();
  return utcYear >= 1 && utcYear <= 9999;
}

module.exports = { isStrictIsoTimestampWithOffset };
