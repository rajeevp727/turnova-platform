const DRIVER_PAYSLIP_FILENAME = /^TurnOva-Payslip-\d{4}-(?:0[1-9]|1[0-2])\.pdf$/;

function isManagedDriverPayslipUri(fileSystem, uri) {
  const directory = fileSystem?.cacheDirectory;
  if (typeof directory !== "string" || directory.length === 0 || typeof uri !== "string" || !uri.startsWith(directory)) {
    return false;
  }

  const filename = uri.slice(directory.length);
  return filename.length > 0 &&
    !filename.includes("/") &&
    !filename.includes("\\") &&
    DRIVER_PAYSLIP_FILENAME.test(filename);
}

async function deleteManagedDriverPayslip(fileSystem, uri) {
  if (!isManagedDriverPayslipUri(fileSystem, uri)) return false;
  await fileSystem.deleteAsync(uri, { idempotent: true });
  return true;
}

async function clearCachedDriverPayslips(fileSystem) {
  const directory = fileSystem?.cacheDirectory;
  if (typeof directory !== "string" || directory.length === 0) {
    return { removed: 0, failed: 0 };
  }

  let entries;
  try {
    entries = await fileSystem.readDirectoryAsync(directory);
  } catch {
    return { removed: 0, failed: 1 };
  }

  const managed = entries.filter(name => DRIVER_PAYSLIP_FILENAME.test(name));
  const results = await Promise.allSettled(
    managed.map(name => fileSystem.deleteAsync(directory + name, { idempotent: true }))
  );
  return {
    removed: results.filter(result => result.status === "fulfilled").length,
    failed: results.filter(result => result.status === "rejected").length
  };
}

async function writeCachedDriverPayslipIfCurrent(fileSystem, uri, base64, isSessionCurrent) {
  if (!isManagedDriverPayslipUri(fileSystem, uri)) {
    throw new Error("Invalid TurnOva payslip cache destination.");
  }
  if (!isSessionCurrent()) return false;

  await fileSystem.writeAsStringAsync(uri, base64, {
    encoding: fileSystem.EncodingType.Base64
  });

  // Logout or expiry can race an in-flight download. If the write finishes after
  // the session changes, remove that exact generated file before returning.
  if (!isSessionCurrent()) {
    try {
      await deleteManagedDriverPayslip(fileSystem, uri);
    } catch {
      throw new Error("Session changed during payslip download and cached file cleanup failed.");
    }
    return false;
  }

  return true;
}

module.exports = {
  isManagedDriverPayslipUri,
  deleteManagedDriverPayslip,
  clearCachedDriverPayslips,
  writeCachedDriverPayslipIfCurrent
};
