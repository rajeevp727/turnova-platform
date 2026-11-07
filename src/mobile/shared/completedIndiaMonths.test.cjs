const assert = require("node:assert/strict");
const test = require("node:test");
const { formatIndiaMonthLabel, getCompletedIndiaMonths } = require("./completedIndiaMonths.js");

test("excludes the current India month at the exact IST month boundary", () => {
  assert.deepEqual(
    getCompletedIndiaMonths(new Date("2026-10-31T18:29:59.999Z"), 3),
    [
      { year: 2026, month: 9 },
      { year: 2026, month: 8 },
      { year: 2026, month: 7 }
    ]
  );

  assert.deepEqual(
    getCompletedIndiaMonths(new Date("2026-10-31T18:30:00.000Z"), 3),
    [
      { year: 2026, month: 10 },
      { year: 2026, month: 9 },
      { year: 2026, month: 8 }
    ]
  );
});

test("rolls back across the India-local year boundary", () => {
  assert.deepEqual(
    getCompletedIndiaMonths(new Date("2026-12-31T18:30:00.000Z"), 3),
    [
      { year: 2026, month: 12 },
      { year: 2026, month: 11 },
      { year: 2026, month: 10 }
    ]
  );
});

test("formats month labels in India time regardless of the device time zone", () => {
  assert.equal(formatIndiaMonthLabel(2026, 10, "en-IN"), "October 2026");
});

test("rejects invalid dates, month values and month-list lengths", () => {
  assert.throws(() => getCompletedIndiaMonths(new Date("invalid")), TypeError);
  assert.throws(() => getCompletedIndiaMonths(new Date(), 0), RangeError);
  assert.throws(() => getCompletedIndiaMonths(new Date(), 13), RangeError);
  assert.throws(() => formatIndiaMonthLabel(2026, 13), RangeError);
});
