"use strict";

const SUPPORT_FIELDS = Object.freeze({
  subject: {
    label: "Support request subject",
    hint: "Enter 3 to 120 characters. Do not include passwords, tokens or payment credentials."
  },
  description: {
    label: "Support request description",
    hint: "Enter 10 to 4000 characters. Describe the issue without including passwords, tokens or payment credentials."
  }
});

const SUPPORT_ACTION = Object.freeze({
  label: "Submit support request",
  busyLabel: "Submitting support request",
  hint: "Sends only a subject and description. No category, priority, assignment or response-time target is selected."
});

function normalizeText(value) {
  return typeof value === "string" ? value.trim() : "";
}

function validateRiderSupportRequest(subject, description) {
  const normalizedSubject = normalizeText(subject);
  const normalizedDescription = normalizeText(description);
  if (normalizedSubject.length < 3 || normalizedSubject.length > 120) {
    return "Subject must contain 3 to 120 characters.";
  }
  if (normalizedDescription.length < 10 || normalizedDescription.length > 4000) {
    return "Description must contain 10 to 4000 characters.";
  }
  return null;
}

function createDefaultIdempotencyKey() {
  return "rider-support-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 14);
}

function isValidIdempotencyKey(value) {
  return typeof value === "string" &&
    value.length > 0 &&
    value.length <= 128 &&
    value === value.trim() &&
    !/[\u0000-\u001f\u007f]/.test(value);
}

function getRiderSupportRequestAttempt(currentAttempt, subject, description, createKey = createDefaultIdempotencyKey) {
  const normalizedSubject = normalizeText(subject);
  const normalizedDescription = normalizeText(description);
  const validation = validateRiderSupportRequest(normalizedSubject, normalizedDescription);
  if (validation) throw new RangeError(validation);

  if (currentAttempt &&
      currentAttempt.subject === normalizedSubject &&
      currentAttempt.description === normalizedDescription &&
      isValidIdempotencyKey(currentAttempt.idempotencyKey)) {
    return currentAttempt;
  }

  const idempotencyKey = createKey();
  if (!isValidIdempotencyKey(idempotencyKey)) {
    throw new Error("Unable to create a valid support request idempotency key.");
  }

  return Object.freeze({
    idempotencyKey,
    subject: normalizedSubject,
    description: normalizedDescription
  });
}

function getRiderSupportFieldProps(field, disabled) {
  const definition = SUPPORT_FIELDS[field];
  if (!definition) throw new RangeError("Unknown Rider support field.");
  return {
    accessibilityLabel: definition.label,
    accessibilityHint: definition.hint,
    accessibilityState: { disabled: Boolean(disabled) }
  };
}

function getRiderSupportActionProps(disabled, busy) {
  return {
    accessibilityRole: "button",
    accessibilityLabel: busy ? SUPPORT_ACTION.busyLabel : SUPPORT_ACTION.label,
    accessibilityHint: SUPPORT_ACTION.hint,
    accessibilityState: { disabled: Boolean(disabled), busy: Boolean(busy) }
  };
}

module.exports = {
  getRiderSupportActionProps,
  getRiderSupportFieldProps,
  getRiderSupportRequestAttempt,
  validateRiderSupportRequest
};
