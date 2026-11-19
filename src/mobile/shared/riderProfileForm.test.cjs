"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const {
  getRiderHelpSourceLabel,
  getRiderProfileActionProps,
  getRiderProfileFieldProps,
  getRiderTabProps,
  getRiderThemeOptionProps,
  normalizeRiderHelpResponse,
  shouldApplyRiderProfileRequestResponse
} = require("./riderProfileForm.js");

test("Rider theme options expose a labelled radio group state", () => {
  const selected = getRiderThemeOptionProps("dark", true);
  assert.equal(selected.accessibilityRole, "radio");
  assert.equal(selected.accessibilityLabel, "Use dark appearance");
  assert.deepEqual(selected.accessibilityState, { checked: true });

  const unselected = getRiderThemeOptionProps("system", false);
  assert.deepEqual(unselected.accessibilityState, { checked: false });
  assert.throws(() => getRiderThemeOptionProps("auto", false), /Unknown Rider theme option/);
});

test("Rider profile help input announces purpose and disabled state", () => {
  const enabled = getRiderProfileFieldProps("helpQuestion", false);
  assert.equal(enabled.accessibilityLabel, "Ask TurnOva help");
  assert.match(enabled.accessibilityHint, /approved TurnOva help content/);
  assert.match(enabled.accessibilityHint, /support escalation/);
  assert.deepEqual(enabled.accessibilityState, { disabled: false });

  const disabled = getRiderProfileFieldProps("helpQuestion", true);
  assert.deepEqual(disabled.accessibilityState, { disabled: true });
  assert.throws(() => getRiderProfileFieldProps("privateNotes", false), /Unknown Rider profile field/);
});

test("Rider help action announces approved content and busy state without implying ride changes", () => {
  const idle = getRiderProfileActionProps("askHelp", false, false);
  assert.equal(idle.accessibilityRole, "button");
  assert.equal(idle.accessibilityLabel, "Ask TurnOva assistant");
  assert.match(idle.accessibilityHint, /cannot book, charge or change a ride/);
  assert.deepEqual(idle.accessibilityState, { disabled: false, busy: false });

  const busy = getRiderProfileActionProps("askHelp", true, true);
  assert.equal(busy.accessibilityLabel, "Checking approved help");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
});

test("Rider sign-out action announces its in-flight state", () => {
  const idle = getRiderProfileActionProps("signOut", false, false);
  assert.equal(idle.accessibilityLabel, "Sign out");
  assert.match(idle.accessibilityHint, /returns to sign in/);
  assert.deepEqual(idle.accessibilityState, { disabled: false, busy: false });

  const busy = getRiderProfileActionProps("signOut", true, true);
  assert.equal(busy.accessibilityLabel, "Signing out");
  assert.deepEqual(busy.accessibilityState, { disabled: true, busy: true });
  assert.throws(() => getRiderProfileActionProps("payment", false, false), /Unknown Rider profile action/);
});

test("Rider navigation tabs expose selected and disabled state", () => {
  const activity = getRiderTabProps("Activity", true, false);
  assert.equal(activity.accessibilityRole, "tab");
  assert.equal(activity.accessibilityLabel, "Activity");
  assert.deepEqual(activity.accessibilityState, { selected: true, disabled: false });

  const blockedBook = getRiderTabProps("Book", false, true);
  assert.equal(blockedBook.accessibilityLabel, "Book rides");
  assert.deepEqual(blockedBook.accessibilityState, { selected: false, disabled: true });
  assert.throws(() => getRiderTabProps("Admin", false, false), /Unknown Rider navigation tab/);
});

test("Rider profile, help, theme and tab controls are wired to accessible helpers", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  for (const id of [
    "rider-profile-title",
    "rider-profile-name",
    "rider-profile-email",
    "rider-profile-role",
    "rider-help-question",
    "rider-help-submit",
    "rider-help-answer",
    "rider-sign-out",
    "rider-global-error",
    "rider-theme-",
    "rider-tab-"
  ]) {
    assert.ok(source.includes(id), "Missing Rider profile/navigation test ID fragment: " + id);
  }
  assert.match(source, /getRiderProfileFieldProps\("helpQuestion",helpBusy\)/);
  assert.match(source, /editable={!helpBusy}/);
  assert.match(source, /getRiderProfileActionProps\("askHelp"/);
  assert.match(source, /getRiderProfileActionProps\("signOut"/);
  assert.match(source, /getRiderThemeOptionProps\(option,themePreference===option\)/);
  assert.match(source, /getRiderTabProps\(item,tab===item/);
  assert.match(source, /accessibilityRole="radiogroup"/);
  assert.match(source, /accessibilityRole="tablist"/);
  assert.match(source, /accessibilityLiveRegion="polite" style={styles.muted}>\{helpAnswer\}/);
});

test("Rider help and sign-out handlers prevent overlapping submissions", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /if\(helpSubmissionInFlight\.current\)return;[\s\S]*helpSubmissionInFlight\.current=true/);
  assert.match(source, /if\(isCurrent\(\)\)\{helpSubmissionInFlight\.current=false;setHelpBusy\(false\);\}/);
  assert.match(source, /if\(logoutInFlight\.current\)return;logoutInFlight\.current=true/);
  assert.match(source, /finally\{logoutInFlight\.current=false;setLogoutBusy\(false\);\}/);
});


test("Rider profile async responses require the latest request and the same authenticated session", () => {
  assert.equal(shouldApplyRiderProfileRequestResponse(2, 2, 7, 7), true);
  assert.equal(shouldApplyRiderProfileRequestResponse(1, 2, 7, 7), false);
  assert.equal(shouldApplyRiderProfileRequestResponse(2, 2, 7, 8), false);
  assert.equal(shouldApplyRiderProfileRequestResponse(0, 0, 7, 7), false);
  assert.equal(shouldApplyRiderProfileRequestResponse(Number.NaN, 1, 7, 7), false);
  assert.equal(shouldApplyRiderProfileRequestResponse(1, 1, -1, -1), false);
});

test("RIDER-HELP-SESSION-001 and RIDER-SUPPORT-SESSION-001: profile responses cannot update or unlock a replacement session", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /riderHelpRequestSequence\.current \+= 1/);
  assert.match(source, /riderSupportRequestSequence\.current \+= 1/);
  assert.match(source, /helpSubmissionInFlight\.current = false/);
  assert.match(source, /supportSubmissionInFlight\.current = false/);
  const helpStart = source.indexOf("const askHelp=async()=>{");
  const supportStart = source.indexOf("const submitSupportRequest=async()=>{", helpStart);
  const logoutStart = source.indexOf("const logout=async()=>{", supportStart);
  assert.ok(helpStart >= 0 && supportStart > helpStart && logoutStart > supportStart);
  const help = source.slice(helpStart, supportStart);
  const support = source.slice(supportStart, logoutStart);
  for (const [handler, sequenceName, commitMarker, lockMarker] of [
    [help, "riderHelpRequestSequence", "setHelpAnswer(`${result.answer}", "helpSubmissionInFlight.current=false;setHelpBusy(false);"],
    [support, "riderSupportRequestSequence", "setSupportNotice(`${result.replayed", "supportSubmissionInFlight.current=false;setSupportBusy(false);"]
  ]) {
    assert.match(handler, /const requestEpoch=sessionEpoch\.current/);
    assert.match(handler, new RegExp("const requestSequence=\\+\\+" + sequenceName + "\\.current"));
    assert.ok(handler.includes("shouldApplyRiderProfileRequestResponse(requestSequence," + sequenceName + ".current,requestEpoch,sessionEpoch.current)"));
    assert.ok(handler.indexOf("if(!isCurrent())return;") < handler.indexOf(commitMarker));
    assert.ok(handler.includes("if(isCurrent()){"+lockMarker+"}"));
  }
});

test("RIDER-DISPATCH-UI-001: Rider help copy does not advertise FIFO ranking or automatic next-driver assignment", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  assert.match(source, /placeholder="Ask about fare agreements, ride statuses or direct-payment reports"/);
  assert.doesNotMatch(source, /\bFIFO ranking\b|automatically assign(?:s|ed|ment)?(?:\s+the)?\s+(?:next\s+)?driver|automatic next[- ]driver assignment/i);
});


test("RIDER-HELP-RESPONSE-001: accept only bounded, allow-listed approved help responses", () => {
  const supported = {
    answer: "The Rider and selected Driver negotiate the fare directly.",
    sourceId: "fare-negotiation",
    escalationRequired: false,
    source: "approved-content",
    providerMetadata: { ignored: true },
    privateReference: "must-not-be-projected"
  };
  assert.deepEqual(normalizeRiderHelpResponse(supported), {
    answer: supported.answer,
    sourceId: "fare-negotiation",
    escalationRequired: false,
    source: "approved-content"
  });
  assert.equal(
    normalizeRiderHelpResponse({ ...supported, source: "provider-approved-content" }).source,
    "provider-approved-content"
  );
  assert.equal(
    normalizeRiderHelpResponse({ ...supported, source: "deterministic-fallback" }).source,
    "deterministic-fallback"
  );
  const unsupported = normalizeRiderHelpResponse({
    answer: "This is outside approved help. Please use the support contact.",
    sourceId: "unsupported",
    escalationRequired: true,
    source: "deterministic-fallback"
  });
  assert.equal(unsupported?.escalationRequired, true);
  assert.equal(unsupported?.sourceId, "unsupported");
});

test("RIDER-HELP-RESPONSE-002: reject malformed, unbounded, unknown or inconsistent help responses", () => {
  const valid = {
    answer: "The Rider and selected Driver negotiate the fare directly.",
    sourceId: "fare-negotiation",
    escalationRequired: false,
    source: "approved-content"
  };
  const invalid = [
    null, [], "help",
    { ...valid, answer: "" },
    { ...valid, answer: "   " },
    { ...valid, answer: " " + valid.answer },
    { ...valid, answer: "x".repeat(1001) },
    { ...valid, answer: "answer\u0000hidden" },
    { ...valid, sourceId: "unknown-entry" },
    { ...valid, source: "arbitrary-provider-text" },
    { ...valid, escalationRequired: "false" },
    { ...valid, escalationRequired: true },
    { ...valid, sourceId: "unsupported" },
    { ...valid, sourceId: "unsupported", escalationRequired: true, source: "provider-approved-content" },
    { ...valid, sourceId: "fare-negotiation", escalationRequired: true }
  ];
  for (const response of invalid) assert.equal(normalizeRiderHelpResponse(response), null);
});

test("Rider help source labels distinguish approved content, provider selection and deterministic fallback", () => {
  assert.equal(getRiderHelpSourceLabel("approved-content"), "Approved in-repository help content");
  assert.equal(getRiderHelpSourceLabel("provider-approved-content"), "AI-selected approved help content");
  assert.equal(getRiderHelpSourceLabel("deterministic-fallback"), "Deterministic fallback from approved help content");
  assert.throws(() => getRiderHelpSourceLabel("unknown"), /Unknown Rider help response source/);
});

test("RIDER-HELP-RESPONSE-001..002: App validates the help contract before rendering answer or source", () => {
  const source = fs.readFileSync(path.join(__dirname, "../rider-app/App.tsx"), "utf8");
  const start = source.indexOf("const askHelp=async()=>{");
  const end = source.indexOf("const submitSupportRequest=async()=>{", start);
  assert.ok(start >= 0 && end > start);
  const handler = source.slice(start, end);
  const fetchIndex = handler.indexOf('const response=await apiFetch("/api/v1/assistance/help"');
  const normalizeIndex = handler.indexOf("normalizeRiderHelpResponse(response)");
  const answerIndex = handler.indexOf("setHelpAnswer(", normalizeIndex);
  assert.ok(fetchIndex >= 0 && normalizeIndex > fetchIndex && answerIndex > normalizeIndex);
  assert.match(handler, /if\(!result\)throw new Error\("Help service returned an invalid response/);
  assert.match(handler, /getRiderHelpSourceLabel\(result\.source\)/);
  assert.doesNotMatch(handler, /as AssistantHelp/);
});
