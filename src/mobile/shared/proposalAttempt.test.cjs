const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const { clearProposalAttempt, clearPersistentProposalAttempt, createProposalAttemptStorageKey, finishProposalSubmission, getOrCreatePersistentProposalAttempt, getOrCreateProposalAttempt } = require("./proposalAttempt.js");

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
  test(role + " proposal submission persists retry keys and blocks concurrent taps", () => {
    const source = fs.readFileSync(appPath, "utf8");
    const handlerStart = source.indexOf("const proposeAgreement");
    const handlerEnd = source.indexOf("const confirmAgreement", handlerStart);
    assert.ok(handlerStart >= 0 && handlerEnd > handlerStart, role + " proposal handler must be present");
    const handler = source.slice(handlerStart, handlerEnd);
    assert.match(handler, /await getOrCreatePersistentProposalAttempt\(\s*SecureStore,\s*proposalAttemptRef/);
    assert.match(handler, /userId:\s*proposalUserId/);
    assert.match(handler, /proposalSubmissionSequence\.current/);
    assert.match(handler, /proposalSubmissionInFlight\.current\s*=\s*attempt\.key/);
    assert.match(handler, /await clearPersistentProposalAttempt\(SecureStore,\s*proposalAttemptRef,\s*attempt\)/);
    assert.match(handler, /finishProposalSubmission\(proposalSubmissionInFlight,\s*ownedSubmissionLock\)/);
    assert.match(handler, /"Idempotency-Key":\s*attempt\.key/);
    assert.doesNotMatch(handler, /Date\.now|Math\.random/);
    assert.ok(handler.indexOf("getOrCreatePersistentProposalAttempt") < handler.indexOf("apiFetch("),
      "the retry key must be durably stored before the proposal POST");
    assert.ok(handler.indexOf("loadAgreementHistory") < handler.indexOf("clearPersistentProposalAttempt"),
      "the persisted key must remain until agreement history refresh succeeds");
    assert.match(source, /proposalSubmitting/);
    assert.match(source, /editable=\{!proposalSubmitting\}/);
    const resetName = role === "Rider" ? "const clearRiderSessionState" : "const clearDriverSessionState";
    const resetStart = source.indexOf(resetName);
    const resetEnd = source.indexOf("useEffect", resetStart);
    assert.ok(resetStart >= 0 && resetEnd > resetStart, role + " session reset must be present");
    const reset = source.slice(resetStart, resetEnd);
    assert.match(reset, /proposalAttemptRef\.current\s*=\s*null/);
    assert.match(reset, /proposalSubmissionInFlight\.current\s*=\s*null/);
    assert.match(reset, /Keep the durable per-user\/ride proposal attempt/);
    assert.match(source, /clearPersistentProposalAttempt\(SecureStore,\s*proposalAttemptRef,\s*previousAttempt\)/);
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


function createSecureStore(options = {}) {
  const values = new Map();
  return {
    values,
    async getItemAsync(key) {
      options.onRead?.(key);
      if (options.failRead) throw new Error("simulated secure-store read failure");
      return values.has(key) ? values.get(key) : null;
    },
    async setItemAsync(key, value) {
      options.onWrite?.(key, value);
      if (options.failWrite) throw new Error("simulated secure-store write failure");
      values.set(key, value);
    },
    async deleteItemAsync(key) {
      options.onDelete?.(key);
      if (options.failDelete) throw new Error("simulated secure-store delete failure");
      values.delete(key);
    }
  };
}

function persistentInput(overrides = {}) {
  return {
    userId: "rider-1",
    rideId: "ride-1",
    actor: "rider",
    amount: 250,
    currency: "INR",
    ...overrides
  };
}

test("persistent proposal attempt restores the same key after an app-process restart", async () => {
  const storage = createSecureStore();
  let generated = 0;
  const keyFactory = actor => actor + "-persisted-" + (++generated);
  const first = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput(), keyFactory);
  const restored = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput(), keyFactory);
  assert.equal(restored.key, first.key);
  assert.equal(restored.fingerprint, first.fingerprint);
  assert.equal(generated, 1);
  assert.equal(storage.values.size, 1);
});

test("persistent proposal storage isolates users and actors while fingerprinting rides", async () => {
  const storage = createSecureStore();
  let generated = 0;
  const keyFactory = actor => actor + "-isolated-" + (++generated);
  const riderOne = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput(), keyFactory);
  const riderTwo = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput({ userId: "rider-2" }), keyFactory);
  const driverOne = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput({ actor: "driver" }), keyFactory);
  const riderOtherRide = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput({ rideId: "ride-2" }), keyFactory);
  assert.notEqual(riderOne.key, riderTwo.key);
  assert.notEqual(riderOne.key, driverOne.key);
  assert.notEqual(riderOne.key, riderOtherRide.key);
  assert.equal(createProposalAttemptStorageKey(persistentInput({ userId: "rider-1" })), createProposalAttemptStorageKey(persistentInput()));
  assert.equal(
    createProposalAttemptStorageKey(persistentInput({ rideId: "ride-2" })),
    createProposalAttemptStorageKey(persistentInput()),
    "one active attempt slot per user/actor avoids unbounded per-ride SecureStore records"
  );
  assert.equal(storage.values.size, 3);
  const riderOtherRideRestored = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput({ rideId: "ride-2" }), keyFactory);
  assert.equal(riderOtherRideRestored.key, riderOtherRide.key);
  assert.equal(generated, 4);
});

test("changed proposal terms replace the persisted key and stale cleanup cannot delete the replacement", async () => {
  const storage = createSecureStore();
  let generated = 0;
  const keyFactory = actor => actor + "-changed-" + (++generated);
  const ref = createRef();
  const first = await getOrCreatePersistentProposalAttempt(storage, ref, persistentInput(), keyFactory);
  const changed = await getOrCreatePersistentProposalAttempt(storage, ref, persistentInput({ amount: 275 }), keyFactory);
  assert.notEqual(changed.key, first.key);
  assert.equal(await clearPersistentProposalAttempt(storage, ref, first), false);
  const restored = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput({ amount: 275 }), keyFactory);
  assert.equal(restored.key, changed.key);
  assert.equal(generated, 2);
});

test("concurrent persistent proposal attempts serialize and reuse one key", async () => {
  const storage = createSecureStore();
  const ref = createRef();
  let generated = 0;
  const keyFactory = actor => actor + "-concurrent-" + (++generated);
  const results = await Promise.all([
    getOrCreatePersistentProposalAttempt(storage, ref, persistentInput(), keyFactory),
    getOrCreatePersistentProposalAttempt(storage, ref, persistentInput(), keyFactory),
    getOrCreatePersistentProposalAttempt(storage, ref, persistentInput(), keyFactory)
  ]);
  assert.equal(new Set(results.map(result => result.key)).size, 1);
  assert.equal(generated, 1);
});

test("secure-store read and write failures fail closed before a proposal can be submitted", async () => {
  const readFailure = createSecureStore({ failRead: true });
  await assert.rejects(
    getOrCreatePersistentProposalAttempt(readFailure, createRef(), persistentInput(), actor => actor + "-read-failure"),
    /No proposal was sent/
  );
  const writeFailure = createSecureStore({ failWrite: true });
  await assert.rejects(
    getOrCreatePersistentProposalAttempt(writeFailure, createRef(), persistentInput(), actor => actor + "-write-failure"),
    /No proposal was sent/
  );
  assert.equal(readFailure.values.size, 0);
  assert.equal(writeFailure.values.size, 0);
});

test("a stale session is rejected before secure-store reads or writes", async () => {
  let reads = 0;
  let writes = 0;
  const storage = createSecureStore({ onRead: () => reads++, onWrite: () => writes++ });
  await assert.rejects(
    getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput(), undefined, () => false),
    /Session changed/
  );
  assert.equal(reads, 0);
  assert.equal(writes, 0);
});

test("successful agreement-history refresh cleanup removes only the matching persisted key", async () => {
  const storage = createSecureStore();
  const ref = createRef();
  let generated = 0;
  const keyFactory = actor => actor + "-clear-" + (++generated);
  const attempt = await getOrCreatePersistentProposalAttempt(storage, ref, persistentInput(), keyFactory);
  assert.equal(await clearPersistentProposalAttempt(storage, ref, attempt), true);
  assert.equal(ref.current, null);
  const nextAttempt = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput(), keyFactory);
  assert.notEqual(nextAttempt.key, attempt.key);
  assert.equal(generated, 2);
});

test("failed secure-store deletion retains the current attempt for safe replay", async () => {
  const storage = createSecureStore({ failDelete: true });
  const ref = createRef();
  const attempt = await getOrCreatePersistentProposalAttempt(storage, ref, persistentInput(), actor => actor + "-delete-failure");
  await assert.rejects(clearPersistentProposalAttempt(storage, ref, attempt), /clear the completed proposal retry key/);
  assert.equal(ref.current.key, attempt.key);
  const restored = await getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput(), () => "must-not-be-used");
  assert.equal(restored.key, attempt.key);
});

test("corrupt stored proposal retry data fails closed instead of generating a new key", async () => {
  const storage = createSecureStore();
  const input = persistentInput();
  const storageKey = createProposalAttemptStorageKey(input);
  storage.values.set(storageKey, "{");
  let generated = 0;
  await assert.rejects(
    getOrCreatePersistentProposalAttempt(storage, createRef(), input, actor => actor + "-corrupt-" + (++generated)),
    /No proposal was sent/
  );
  assert.equal(generated, 0);
  assert.equal(storage.values.get(storageKey), "{");
});


test("a session change during secure-store read stops before writing or sending", async () => {
  let current = true;
  let writes = 0;
  const storage = createSecureStore({
    onRead: () => { current = false; },
    onWrite: () => { writes += 1; }
  });
  await assert.rejects(
    getOrCreatePersistentProposalAttempt(storage, createRef(), persistentInput(), undefined, () => current),
    /Session changed/
  );
  assert.equal(writes, 0);
  assert.equal(storage.values.size, 0);
});

test("a session change during secure-store write stops POST while retaining the same key for safe replay", async () => {
  let current = true;
  let generated = 0;
  const storage = createSecureStore({ onWrite: () => { current = false; } });
  await assert.rejects(
    getOrCreatePersistentProposalAttempt(
      storage,
      createRef(),
      persistentInput(),
      actor => actor + "-write-race-" + (++generated),
      () => current
    ),
    /Session changed/
  );
  assert.equal(storage.values.size, 1);
  const restored = await getOrCreatePersistentProposalAttempt(
    storage,
    createRef(),
    persistentInput(),
    () => "must-not-be-used",
    () => true
  );
  assert.equal(restored.key, "rider-write-race-1");
  assert.equal(generated, 1);
});

test("a corrupt record is deleted only when the completed in-memory attempt still owns its key", async () => {
  const storage = createSecureStore();
  const ref = createRef();
  const attempt = await getOrCreatePersistentProposalAttempt(
    storage, ref, persistentInput(), actor => actor + "-corrupt-cleanup"
  );
  const storageKey = createProposalAttemptStorageKey(persistentInput());
  storage.values.set(storageKey, "{");
  assert.equal(await clearPersistentProposalAttempt(storage, ref, attempt), true);
  assert.equal(ref.current, null);
  assert.equal(storage.values.has(storageKey), false);

  storage.values.set(storageKey, "{");
  assert.equal(await clearPersistentProposalAttempt(storage, createRef(), attempt), false);
  assert.equal(storage.values.get(storageKey), "{");
});
