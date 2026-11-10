const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const { clearProposalAttempt, finishProposalSubmission, getOrCreateProposalAttempt } = require("./proposalAttempt.js");

function createRef() {
  return { current: null };
}

test("identical proposal retries reuse the same key after an ambiguous request failure", () => {
  const ref = createRef();
  let generated = 0;
  const keyFactory = actor => actor + "-attempt-" + (++generated);
  const input = { rideId: "ride-1", actor: "rider", amount: 250, currency: "INR" };
  const first = getOrCreateProposalAttempt(ref, input, keyFactory);
  // An ambiguous network error intentionally leaves the attempt available for retry.
  const retry = getOrCreateProposalAttempt(ref, input, keyFactory);
  assert.equal(first.key, "rider-attempt-1");
  assert.equal(retry.key, first.key);
  assert.equal(generated, 1);
});

test("changing amount or ride creates a new proposal idempotency key", () => {
  const ref = createRef();
  let generated = 0;
  const keyFactory = actor => actor + "-attempt-" + (++generated);
  const first = getOrCreateProposalAttempt(ref, { rideId: "ride-1", actor: "driver", amount: 250 }, keyFactory);
  const changedAmount = getOrCreateProposalAttempt(ref, { rideId: "ride-1", actor: "driver", amount: 275 }, keyFactory);
  const changedRide = getOrCreateProposalAttempt(ref, { rideId: "ride-2", actor: "driver", amount: 275 }, keyFactory);
  assert.notEqual(changedAmount.key, first.key);
  assert.notEqual(changedRide.key, changedAmount.key);
  assert.equal(generated, 3);
});

test("a late completion cannot clear a newer proposal attempt", () => {
  const ref = createRef();
  let generated = 0;
  const keyFactory = actor => actor + "-attempt-" + (++generated);
  const oldAttempt = getOrCreateProposalAttempt(ref, { rideId: "ride-1", actor: "rider", amount: 100 }, keyFactory);
  const newAttempt = getOrCreateProposalAttempt(ref, { rideId: "ride-2", actor: "rider", amount: 100 }, keyFactory);
  assert.equal(clearProposalAttempt(ref, oldAttempt.key), false);
  assert.equal(ref.current.key, newAttempt.key);
  assert.equal(clearProposalAttempt(ref, newAttempt.key), true);
  assert.equal(ref.current, null);
});

test("a stale proposal completion cannot release a newer in-flight submission", () => {
  const inFlight = { current: "new-attempt" };
  assert.equal(finishProposalSubmission(inFlight, "old-attempt"), false);
  assert.equal(inFlight.current, "new-attempt");
  assert.equal(finishProposalSubmission(inFlight, "new-attempt"), true);
  assert.equal(inFlight.current, null);
});

test("confirmed success clears the attempt so an intentional new proposal gets a fresh key", () => {
  const ref = createRef();
  let generated = 0;
  const keyFactory = actor => actor + "-attempt-" + (++generated);
  const input = { rideId: "ride-1", actor: "rider", amount: 100 };
  const first = getOrCreateProposalAttempt(ref, input, keyFactory);
  assert.equal(clearProposalAttempt(ref, first.key), true);
  const nextVersion = getOrCreateProposalAttempt(ref, input, keyFactory);
  assert.notEqual(nextVersion.key, first.key);
  assert.equal(generated, 2);
});

test("proposal attempts reject missing refs, unsupported actors and invalid amounts", () => {
  assert.throws(() => getOrCreateProposalAttempt(null, { rideId: "ride-1", actor: "rider", amount: 1 }), /ref/i);
  assert.throws(() => getOrCreateProposalAttempt(createRef(), { rideId: "ride-1", actor: "admin", amount: 1 }), /actor/i);
  assert.throws(() => getOrCreateProposalAttempt(createRef(), { rideId: "ride-1", actor: "rider", amount: 0 }), /amount/i);
});

for (const [role, appPath] of [
  ["Rider", path.join(__dirname, "../rider-app/App.tsx")],
  ["Driver", path.join(__dirname, "../driver-app/App.tsx")]
]) {
  test(role + " proposal submission uses stable attempts and blocks concurrent taps", () => {
    const source = fs.readFileSync(appPath, "utf8");
    const handlerStart = source.indexOf("const proposeAgreement");
    const handlerEnd = source.indexOf("const confirmAgreement", handlerStart);
    assert.ok(handlerStart >= 0 && handlerEnd > handlerStart, role + " proposal handler must be present");
    const handler = source.slice(handlerStart, handlerEnd);
    assert.match(handler, /getOrCreateProposalAttempt\(proposalAttemptRef/);
    assert.match(handler, /clearProposalAttempt\(proposalAttemptRef/);
    assert.match(handler, /proposalSubmissionInFlight\.current/);
    assert.match(handler, /proposalSubmissionInFlight\.current\s*=\s*attempt\.key/);
    assert.match(handler, /finishProposalSubmission\(proposalSubmissionInFlight,\s*attempt\.key\)/);
    assert.match(handler, /"Idempotency-Key":\s*attempt\.key/);
    assert.doesNotMatch(handler, /Date\.now|Math\.random/);
    assert.ok(handler.indexOf("loadAgreementHistory") < handler.indexOf("clearProposalAttempt"),
      "attempt must remain available until agreement history refresh succeeds");
    assert.match(source, /proposalSubmitting/);
    assert.match(source, /editable=\{!proposalSubmitting\}/);
    const resetName = role === "Rider" ? "const clearRiderSessionState" : "const clearDriverSessionState";
    const resetStart = source.indexOf(resetName);
    const resetEnd = source.indexOf("useEffect", resetStart);
    assert.ok(resetStart >= 0 && resetEnd > resetStart, role + " session reset must be present");
    const reset = source.slice(resetStart, resetEnd);
    assert.match(reset, /proposalAttemptRef\.current\s*=\s*null/);
    assert.match(reset, /proposalSubmissionInFlight\.current\s*=\s*null/);
    assert.match(reset, /setProposalSubmitting\(false\)/);
    assert.match(source, /proposalRideIdRef/);
  });
}


test("changing actor or currency creates a new proposal attempt key", () => {
  const ref = createRef();
  const generatedActors = [];
  const keyFactory = actor => {
    generatedActors.push(actor);
    return `${actor}-proposal-${generatedActors.length}`;
  };

  const riderInr = getOrCreateProposalAttempt(
    ref,
    { rideId: "ride-1", actor: "rider", amount: 450, currency: "INR" },
    keyFactory
  );
  const driverInr = getOrCreateProposalAttempt(
    ref,
    { rideId: "ride-1", actor: "driver", amount: 450, currency: "INR" },
    keyFactory
  );
  const driverUsd = getOrCreateProposalAttempt(
    ref,
    { rideId: "ride-1", actor: "driver", amount: 450, currency: "USD" },
    keyFactory
  );

  assert.notEqual(driverInr.key, riderInr.key);
  assert.notEqual(driverUsd.key, driverInr.key);
  assert.deepEqual(generatedActors, ["rider", "driver", "driver"]);
});
