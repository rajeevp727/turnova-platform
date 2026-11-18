"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  getRiderSupportActionProps,
  getRiderSupportFieldProps,
  getRiderSupportRequestAttempt,
  validateRiderSupportRequest
} = require("./riderSupportRequest.js");

test("Rider support request validates trimmed subject and description bounds", () => {
  assert.equal(validateRiderSupportRequest("  Trip issue  ", "  The trip status did not refresh after reconnecting.  "), null);
  assert.equal(validateRiderSupportRequest("ab", "A sufficiently long description."), "Subject must contain 3 to 120 characters.");
  assert.equal(validateRiderSupportRequest("Valid subject", "short"), "Description must contain 10 to 4000 characters.");
  assert.equal(validateRiderSupportRequest("x".repeat(121), "A sufficiently long description."), "Subject must contain 3 to 120 characters.");
  assert.equal(validateRiderSupportRequest("Valid subject", "x".repeat(4001)), "Description must contain 10 to 4000 characters.");
});

test("Rider support fields expose labels, privacy hints and disabled state", () => {
  const subject = getRiderSupportFieldProps("subject", false);
  assert.equal(subject.accessibilityLabel, "Support request subject");
  assert.match(subject.accessibilityHint, /Do not include passwords, tokens or payment credentials/);
  assert.deepEqual(subject.accessibilityState, { disabled: false });

  const description = getRiderSupportFieldProps("description", true);
  assert.equal(description.accessibilityLabel, "Support request description");
  assert.deepEqual(description.accessibilityState, { disabled: true });
  assert.throws(() => getRiderSupportFieldProps("category", false), /Unknown Rider support field/);
});

test("Rider support submit action exposes neutral wording and busy state", () => {
  const idle = getRiderSupportActionProps(false, false);
  assert.equal(idle.accessibilityRole, "button");
  assert.equal(idle.accessibilityLabel, "Submit support request");
  assert.match(idle.accessibilityHint, /No category, priority, assignment or response-time target/);
  assert.deepEqual(idle.accessibilityState, { disabled: false, busy: false });

  const busy = getRiderSupportActionProps(true, true);
  assert.equal(busy.accessibilityLabel, "Submitting support request");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
});

test("Rider support request attempt trims content and reuses the same key for an unchanged retry", () => {
  let generated = 0;
  const createKey = () => "rider-support-test-" + (++generated);
  const first = getRiderSupportRequestAttempt(null, "  Trip issue  ", "  The trip status did not refresh after reconnecting.  ", createKey);
  const retry = getRiderSupportRequestAttempt(first, "Trip issue", "The trip status did not refresh after reconnecting.", createKey);

  assert.equal(first.idempotencyKey, "rider-support-test-1");
  assert.equal(retry.idempotencyKey, first.idempotencyKey);
  assert.equal(generated, 1);
  assert.equal(first.subject, "Trip issue");
  assert.equal(first.description, "The trip status did not refresh after reconnecting.");
});

test("Rider support request attempt creates a new key when edited content changes", () => {
  let generated = 0;
  const createKey = () => "rider-support-test-" + (++generated);
  const first = getRiderSupportRequestAttempt(null, "Trip issue", "The trip status did not refresh after reconnecting.", createKey);
  const changed = getRiderSupportRequestAttempt(first, "Payment display issue", "The trip status did not refresh after reconnecting.", createKey);

  assert.notEqual(changed.idempotencyKey, first.idempotencyKey);
  assert.equal(generated, 2);
  assert.equal(changed.subject, "Payment display issue");
});

test("Rider support request attempt rejects invalid idempotency keys", () => {
  assert.throws(
    () => getRiderSupportRequestAttempt(null, "Trip issue", "The trip status did not refresh after reconnecting.", () => "bad\r\nkey"),
    /valid support request idempotency key/
  );
});

test("Rider support request screen wiring uses only the neutral authenticated API contract", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  for (const id of [
    "rider-support-subject",
    "rider-support-description",
    "rider-support-submit",
    "rider-support-notice",
    "rider-support-error"
  ]) {
    assert.ok(source.includes(id), "Missing Rider support test ID: " + id);
  }
  assert.match(source, /apiFetch\("\/api\/v1\/support\/requests"/);
  assert.match(source, /"Idempotency-Key":attempt\.idempotencyKey/);
  assert.match(source, /JSON\.stringify\(\{subject:attempt\.subject,description:attempt\.description\}\)/);
  assert.doesNotMatch(source, /supportCategory|supportPriority|supportSeverity|responseTimeTarget/);
});

test("Rider support submission is single-flight and preserves the attempt after failure", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /if\(supportSubmissionInFlight\.current\)return/);
  assert.match(source, /supportAttemptRef\.current=attempt/);
  assert.match(source, /supportAttemptRef\.current=null;setSupportSubject\(""\);setSupportDescription\(""\)/);
  assert.match(source, /finally\{supportSubmissionInFlight\.current=false;setSupportBusy\(false\);\}/);
  assert.match(source, /If you retry without changing the text, the same idempotency key is reused/);
  assert.match(source, /getRiderProfileActionProps\("signOut",logoutBusy\|\|helpBusy\|\|supportBusy,logoutBusy\)/);
  assert.match(source, /disabled=\{logoutBusy\|\|helpBusy\|\|supportBusy\}/);
  assert.match(source, /style=\{\[styles\.muted,\{color:colors\.success\}\]\}\>\{supportNotice\}/);
  assert.doesNotMatch(source, /styles\.success/);
});
