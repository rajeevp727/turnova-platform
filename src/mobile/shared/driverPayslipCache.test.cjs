const assert = require("node:assert/strict");
const test = require("node:test");
const {
  isManagedDriverPayslipUri,
  deleteManagedDriverPayslip,
  clearCachedDriverPayslips,
  writeCachedDriverPayslipIfCurrent
} = require("./driverPayslipCache.js");

const CACHE = "file:///cache/";

function createFileSystem(initialFiles = [], hooks = {}) {
  const files = new Set(initialFiles);
  const deleted = [];
  const writes = [];
  return {
    cacheDirectory: CACHE,
    EncodingType: { Base64: "base64" },
    files,
    deleted,
    writes,
    async readDirectoryAsync(directory) {
      assert.equal(directory, CACHE);
      if (hooks.read) await hooks.read(directory);
      return [...files];
    },
    async deleteAsync(uri, options) {
      deleted.push({ uri, options });
      if (hooks.delete) await hooks.delete(uri);
      files.delete(uri.slice(CACHE.length));
    },
    async writeAsStringAsync(uri, contents, options) {
      writes.push({ uri, contents, options });
      if (hooks.write) await hooks.write(uri, contents, options);
      files.add(uri.slice(CACHE.length));
    }
  };
}

test("recognizes only exact generated TurnOva monthly payslip cache URIs", () => {
  const fileSystem = createFileSystem();

  assert.equal(isManagedDriverPayslipUri(fileSystem, CACHE + "TurnOva-Payslip-2026-10.pdf"), true);
  assert.equal(isManagedDriverPayslipUri(fileSystem, CACHE + "TurnOva-Payslip-2026-01.pdf"), true);
  assert.equal(isManagedDriverPayslipUri(fileSystem, CACHE + "TurnOva-Payslip-2026-13.pdf"), false);
  assert.equal(isManagedDriverPayslipUri(fileSystem, CACHE + "TurnOva-Payslip-26-10.pdf"), false);
  assert.equal(isManagedDriverPayslipUri(fileSystem, CACHE + "other.pdf"), false);
  assert.equal(isManagedDriverPayslipUri(fileSystem, CACHE + "../TurnOva-Payslip-2026-10.pdf"), false);
  assert.equal(isManagedDriverPayslipUri(fileSystem, "file:///other/TurnOva-Payslip-2026-10.pdf"), false);
});

test("logout cleanup removes only exact TurnOva payslip PDFs and preserves unrelated cache files", async () => {
  const fileSystem = createFileSystem([
    "TurnOva-Payslip-2026-10.pdf",
    "TurnOva-Payslip-2026-09.pdf",
    "TurnOva-Payslip-2026-13.pdf",
    "TurnOva-Payslip-2026-10.pdf.tmp",
    "statement.pdf",
    "profile-photo.jpg"
  ]);

  const result = await clearCachedDriverPayslips(fileSystem);

  assert.deepEqual(result, { removed: 2, failed: 0 });
  assert.deepEqual([...fileSystem.files].sort(), [
    "TurnOva-Payslip-2026-10.pdf.tmp",
    "TurnOva-Payslip-2026-13.pdf",
    "profile-photo.jpg",
    "statement.pdf"
  ]);
  assert.deepEqual(fileSystem.deleted.map(item => item.uri).sort(), [
    CACHE + "TurnOva-Payslip-2026-09.pdf",
    CACHE + "TurnOva-Payslip-2026-10.pdf"
  ]);
  assert.ok(fileSystem.deleted.every(item => item.options.idempotent === true));
});

test("cache directory read failure is reported without deleting unrelated files", async () => {
  const fileSystem = createFileSystem(["TurnOva-Payslip-2026-10.pdf", "statement.pdf"], {
    read: async () => { throw new Error("cache unavailable"); }
  });

  assert.deepEqual(await clearCachedDriverPayslips(fileSystem), { removed: 0, failed: 1 });
  assert.equal(fileSystem.files.size, 2);
  assert.equal(fileSystem.deleted.length, 0);
});

test("cleanup reports partial deletion failures while attempting every generated payslip", async () => {
  const fileSystem = createFileSystem([
    "TurnOva-Payslip-2026-10.pdf",
    "TurnOva-Payslip-2026-09.pdf"
  ], {
    delete: async uri => {
      if (uri.endsWith("2026-09.pdf")) throw new Error("delete denied");
    }
  });

  assert.deepEqual(await clearCachedDriverPayslips(fileSystem), { removed: 1, failed: 1 });
  assert.equal(fileSystem.deleted.length, 2);
  assert.equal(fileSystem.files.has("TurnOva-Payslip-2026-10.pdf"), false);
  assert.equal(fileSystem.files.has("TurnOva-Payslip-2026-09.pdf"), true);
});

test("targeted cleanup refuses paths outside the cache or outside the generated filename contract", async () => {
  const fileSystem = createFileSystem(["TurnOva-Payslip-2026-10.pdf"]);

  assert.equal(await deleteManagedDriverPayslip(fileSystem, "file:///other/TurnOva-Payslip-2026-10.pdf"), false);
  assert.equal(await deleteManagedDriverPayslip(fileSystem, CACHE + "../TurnOva-Payslip-2026-10.pdf"), false);
  assert.equal(await deleteManagedDriverPayslip(fileSystem, CACHE + "statement.pdf"), false);
  assert.equal(fileSystem.deleted.length, 0);
  assert.equal(fileSystem.files.size, 1);
});

test("write is skipped when the initiating session is already stale", async () => {
  const fileSystem = createFileSystem();

  const written = await writeCachedDriverPayslipIfCurrent(
    fileSystem, CACHE + "TurnOva-Payslip-2026-10.pdf", "JVBERi0=", () => false
  );

  assert.equal(written, false);
  assert.equal(fileSystem.writes.length, 0);
  assert.equal(fileSystem.files.size, 0);
});

test("in-flight download removes its exact PDF if logout occurs while the file write is pending", async () => {
  let releaseWrite;
  const writeGate = new Promise(resolve => { releaseWrite = resolve; });
  const fileSystem = createFileSystem([], {
    write: async () => { await writeGate; }
  });
  let sessionCurrent = true;

  const pending = writeCachedDriverPayslipIfCurrent(
    fileSystem,
    CACHE + "TurnOva-Payslip-2026-10.pdf",
    "JVBERi0=",
    () => sessionCurrent
  );

  sessionCurrent = false;
  releaseWrite();
  const written = await pending;

  assert.equal(written, false);
  assert.equal(fileSystem.files.has("TurnOva-Payslip-2026-10.pdf"), false);
  assert.deepEqual(fileSystem.deleted.map(item => item.uri), [
    CACHE + "TurnOva-Payslip-2026-10.pdf"
  ]);
});

test("current session writes a generated payslip using Base64 encoding", async () => {
  const fileSystem = createFileSystem();

  const written = await writeCachedDriverPayslipIfCurrent(
    fileSystem, CACHE + "TurnOva-Payslip-2026-10.pdf", "JVBERi0=", () => true
  );

  assert.equal(written, true);
  assert.deepEqual(fileSystem.writes, [{
    uri: CACHE + "TurnOva-Payslip-2026-10.pdf",
    contents: "JVBERi0=",
    options: { encoding: "base64" }
  }]);
  assert.equal(fileSystem.files.has("TurnOva-Payslip-2026-10.pdf"), true);
});

test("missing cache directory is a safe no-op", async () => {
  const fileSystem = createFileSystem();
  fileSystem.cacheDirectory = null;

  assert.deepEqual(await clearCachedDriverPayslips(fileSystem), { removed: 0, failed: 0 });
  assert.equal(await deleteManagedDriverPayslip(fileSystem, CACHE + "TurnOva-Payslip-2026-10.pdf"), false);
});
