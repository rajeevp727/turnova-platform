"use strict";
const fs = require("node:fs");
const net = require("node:net");
const path = require("node:path");

const APP_CONTRACTS = Object.freeze({
  "turnova-rider": Object.freeze({ name: "TurnOva Rider", androidPackage: "com.turnova.rider" }),
  "turnova-driver": Object.freeze({ name: "TurnOva Driver", androidPackage: "com.turnova.driver" })
});
const SEMVER_PATTERN = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$/;
const ANDROID_PACKAGE_PATTERN = /^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+$/;
const IOS_BUNDLE_PATTERN = /^[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$/;
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function isValidSemanticVersion(value) {
  return typeof value === "string" && SEMVER_PATTERN.test(value) && !/[\r\n]/.test(value);
}

function supportsEasCli16OrNewer(range) {
  if (typeof range !== "string" || /[\r\n]/.test(range)) return false;
  const match = /^>=\s*(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\s*$/.exec(range);
  return Boolean(match) && BigInt(match[1]) >= 16n;
}

function isNonPublicIpv4(host) {
  if (net.isIP(host) !== 4) return false;
  const [a, b, c] = host.split(".").map(Number);
  return a === 0 || a === 10 || a === 127 ||
    (a === 100 && b >= 64 && b <= 127) ||
    (a === 169 && b === 254) ||
    (a === 172 && b >= 16 && b <= 31) ||
    (a === 192 && b === 0 && (c === 0 || c === 2)) ||
    (a === 192 && b === 88 && c === 99) ||
    (a === 192 && b === 168) ||
    (a === 198 && (b === 18 || b === 19)) ||
    (a === 198 && b === 51 && c === 100) ||
    (a === 203 && b === 0 && c === 113) ||
    a >= 224;
}

function expandIpv6Hextets(address) {
  let normalized = address.toLowerCase();
  if (normalized.includes(".")) {
    const lastColon = normalized.lastIndexOf(":");
    const octets = normalized.slice(lastColon + 1).split(".").map(Number);
    if (octets.length !== 4 || octets.some(value => !Number.isInteger(value) || value < 0 || value > 255)) return null;
    const high = ((octets[0] << 8) | octets[1]).toString(16);
    const low = ((octets[2] << 8) | octets[3]).toString(16);
    normalized = normalized.slice(0, lastColon + 1) + high + ":" + low;
  }
  const halves = normalized.split("::");
  if (halves.length > 2) return null;
  const left = halves[0] ? halves[0].split(":") : [];
  const right = halves.length === 2 && halves[1] ? halves[1].split(":") : [];
  const zeroCount = 8 - left.length - right.length;
  if (zeroCount < 0 || (halves.length === 1 && zeroCount !== 0)) return null;
  const parts = [...left, ...Array(zeroCount).fill("0"), ...right];
  if (parts.length !== 8) return null;
  const values = parts.map(part => Number.parseInt(part || "0", 16));
  return values.every(value => Number.isInteger(value) && value >= 0 && value <= 0xffff) ? values : null;
}

function isNonPublicIpv6(host) {
  const address = host.startsWith("[") && host.endsWith("]") ? host.slice(1, -1) : host;
  if (net.isIP(address) !== 6) return false;
  const parts = expandIpv6Hextets(address);
  if (!parts) return true;
  const [first, second] = parts;
  const globalUnicast = (first & 0xe000) === 0x2000;
  const specialPurpose = first === 0x2001 && second <= 0x01ff;
  const documentation = (first === 0x2001 && second === 0x0db8) ||
    (first === 0x3fff && (second & 0xfff0) === 0) ||
    first === 0x2002;
  return !globalUnicast || specialPurpose || documentation;
}

function isInternalHostname(host) {
  const normalized = host.toLowerCase().endsWith(".") ? host.slice(0, -1).toLowerCase() : host.toLowerCase();
  const internalSuffixes = [
    ".localhost", ".local", ".localdomain", ".internal", ".lan",
    ".home", ".home.arpa", ".test", ".invalid", ".intranet", ".private", ".example",
    ".example.com", ".example.net", ".example.org", ".onion"
  ];
  const reservedExampleHosts = new Set(["example.com", "example.net", "example.org"]);
  return normalized === "localhost" || reservedExampleHosts.has(normalized) || !normalized.includes(".") ||
    internalSuffixes.some(suffix => normalized.endsWith(suffix));
}

function validateReleaseConfig({ appJson, easJson, appDirectory, apiBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL, exists = fs.existsSync }) {
  const errors = [];
  const externalGates = [];
  const expo = appJson && appJson.expo ? appJson.expo : {};
  const slug = typeof expo.slug === "string" ? expo.slug : "";
  const contract = APP_CONTRACTS[slug];
  const root = path.resolve(appDirectory || process.cwd());

  const error = (id, message) => errors.push({ id, message });
  const blocker = (id, owner, requiredInput) => {
    if (!externalGates.some(item => item.id === id)) externalGates.push({ id, owner, requiredInput });
  };

  if (!contract) error("app-slug", "expo.slug must be turnova-rider or turnova-driver.");
  else if (expo.name !== contract.name) error("app-name", "expo.name does not match the Rider/Driver app identity for this slug.");

  if (!isValidSemanticVersion(expo.version)) {
    error("app-version", "expo.version must be a valid semantic version.");
  }

  if (!contract || typeof expo.android?.package !== "string" ||
      !ANDROID_PACKAGE_PATTERN.test(expo.android.package) ||
      expo.android.package !== contract.androidPackage) {
    error("android-package", "expo.android.package must match the established package ID for this app.");
  }

  if (typeof expo.ios?.supportsTablet !== "boolean") {
    error("ios-device-family", "expo.ios.supportsTablet must be explicitly configured.");
  }

  if (typeof expo.android?.adaptiveIcon?.backgroundColor !== "string" ||
      !/^#[0-9a-f]{6}$/i.test(expo.android.adaptiveIcon.backgroundColor)) {
    error("android-adaptive-background", "Android adaptive icon background must be a six-digit hex color.");
  }

  function checkAsset(label, relativePath) {
    if (typeof relativePath !== "string" || relativePath.trim().length === 0) {
      error(label, "The configured app icon path is missing.");
      return;
    }
    const resolved = path.resolve(root, relativePath);
    const relative = path.relative(root, resolved);
    if (path.isAbsolute(relative) || relative === ".." || relative.startsWith(".." + path.sep)) {
      error(label, "App icon paths must remain inside the app directory.");
      return;
    }
    if (!exists(resolved)) error(label, "The configured app icon does not exist after the package postinstall step.");
  }

  checkAsset("app-icon", expo.icon);
  checkAsset("ios-icon", expo.ios?.icon || expo.icon);
  checkAsset("android-adaptive-icon", expo.android?.adaptiveIcon?.foregroundImage);

  const productionApiBaseUrlConfiguredInCi = (() => {
    if (typeof apiBaseUrl !== "string" || apiBaseUrl.trim().length === 0) return false;
    try {
      const parsed = new URL(apiBaseUrl);
      const host = parsed.hostname.toLowerCase();
      const unbracketedHost = host.startsWith("[") && host.endsWith("]") ? host.slice(1, -1) : host;
      const ipVersion = net.isIP(unbracketedHost);
      const nonPublicIp = ipVersion === 4 ? isNonPublicIpv4(unbracketedHost) : ipVersion === 6 ? isNonPublicIpv6(host) : false;
      const internalHost = ipVersion === 0 && isInternalHostname(unbracketedHost);
      return parsed.protocol === "https:" && host.length > 0 && !nonPublicIp && !internalHost &&
        !host.includes("<") && !host.includes(">") && parsed.username.length === 0 && parsed.password.length === 0 &&
        parsed.search.length === 0 && parsed.hash.length === 0;
    } catch { return false; }
  })();

  const bundleIdentifier = expo.ios?.bundleIdentifier;
  let iosBundleIdentifierSyntaxValid = false;
  if (typeof bundleIdentifier !== "string" || bundleIdentifier.trim().length === 0) {
    blocker("ios-bundle-registration", "Product / App Owner", "Provide the approved reverse-DNS iOS bundle identifier for this app and register it with Apple.");
  } else if (!IOS_BUNDLE_PATTERN.test(bundleIdentifier) ||
      bundleIdentifier.toLowerCase() === "com.example" ||
      bundleIdentifier.toLowerCase().startsWith("com.example.")) {
    error("ios-bundle-identifier", "The configured iOS bundle identifier is malformed or a placeholder.");
  } else {
    iosBundleIdentifierSyntaxValid = true;
    blocker("ios-bundle-registration", "Product / App Owner", "Verify that the configured iOS bundle identifier is approved and registered with Apple; syntax validation does not prove registration.");
  }

  const projectId = expo.extra?.eas?.projectId;
  let easProjectIdSyntaxValid = false;
  if (typeof projectId !== "string" || projectId.trim().length === 0) {
    blocker("eas-project-linkage", "Release Engineering", "Add the approved EAS project ID to this app and verify the app is linked to that project.");
  } else if (!UUID_PATTERN.test(projectId)) {
    error("eas-project-id", "expo.extra.eas.projectId must be a valid UUID.");
  } else {
    easProjectIdSyntaxValid = true;
    blocker("eas-project-linkage", "Release Engineering", "Verify that the configured UUID resolves to the approved EAS project and this app is linked; UUID syntax alone is not linkage evidence.");
  }

  const cliVersion = easJson?.cli?.version;
  if (!supportsEasCli16OrNewer(cliVersion)) {
    error("eas-cli-version", "eas.json must require EAS CLI 16 or newer.");
  }

  const profiles = easJson?.build || {};
  const development = profiles.development;
  const preview = profiles.preview;
  const production = profiles.production;
  const appVersionSource = easJson?.cli?.appVersionSource;
  const appVersionSourceValid = appVersionSource === "local" || appVersionSource === "remote";
  if (appVersionSource !== undefined && !appVersionSourceValid) {
    error("build-number-source", "eas.json cli.appVersionSource must be local or remote.");
  }
  if (development?.developmentClient !== true || development?.distribution !== "internal") {
    error("development-profile", "The development profile must be an internal development-client build.");
  }
  if (preview?.distribution !== "internal" || preview?.android?.buildType !== "apk") {
    error("preview-profile", "The preview profile must produce an internally distributed Android APK.");
  }
  if (production?.android?.buildType !== "app-bundle" || production?.ios?.distribution !== "store") {
    error("production-profile", "The production profile must target an Android app bundle and iOS store distribution.");
  }
  const productionAutoIncrementEnabled = production?.autoIncrement === true;
  const buildNumberPolicyConfigured = appVersionSourceValid && productionAutoIncrementEnabled;
  blocker("build-number-policy", "Release Engineering", buildNumberPolicyConfigured
    ? "Approve the configured local/remote version source and automatic increment policy, then verify actual EAS build metadata."
    : "Approve local or remote version ownership, set cli.appVersionSource and production.autoIncrement=true, then verify actual EAS build metadata.");

  blocker("production-api-base-url", "Platform / Infrastructure", productionApiBaseUrlConfiguredInCi
    ? "Verify the same approved HTTPS URL is present in the EAS production environment, resolves publicly and passes TLS, CORS and health/readiness checks. GitHub CI cannot inspect EAS remote variables or resolve DNS from that environment."
    : "Set the approved HTTPS URL in the EAS production environment and verify public DNS, TLS, CORS and health/readiness. GitHub CI cannot inspect EAS remote variables or verify DNS from EAS.");
  blocker("signing-credentials", "Release Owner", "Verify Android upload signing and Apple distribution certificate/provisioning credentials through EAS secure credentials; never commit them.");
  blocker("physical-device-acceptance", "QA / Product Owner", "Install signed Rider and Driver builds on the agreed Android/iOS device matrix and record functional, security and pilot acceptance.");
  blocker("store-submission", "Release Owner / Legal", "Complete store metadata, privacy disclosures, review submission and approval for both apps.");

  const engineeringPassed = errors.length === 0;
  const ciVisibleReleaseConfigurationComplete = engineeringPassed && productionApiBaseUrlConfiguredInCi &&
    iosBundleIdentifierSyntaxValid && easProjectIdSyntaxValid && buildNumberPolicyConfigured;

  return {
    schemaVersion: 2,
    app: {
      slug: slug || null,
      name: typeof expo.name === "string" ? expo.name : null,
      version: typeof expo.version === "string" ? expo.version : null,
      androidPackage: typeof expo.android?.package === "string" ? expo.android.package : null,
      iosBundleIdentifier: typeof bundleIdentifier === "string" ? bundleIdentifier : null,
      iosBundleIdentifierSyntaxValid,
      easProjectIdConfigured: typeof projectId === "string" && projectId.trim().length > 0,
      easProjectIdSyntaxValid,
      appVersionSource: appVersionSourceValid ? appVersionSource : null,
      productionAutoIncrementEnabled,
      productionApiBaseUrlConfiguredInCi
    },
    engineeringChecks: { status: engineeringPassed ? "passed" : "failed", errors },
    ciVisibleReleaseConfigurationComplete,
    productionReadiness: externalGates.length === 0 && engineeringPassed ? "not_verified" : "blocked",
    externalGates,
    currentCiArtifactEvidence: {
      android: "debug-apk-and-standalone-release-apk-variant",
      ios: "unsigned-ios-simulator-app-only",
      signedAndroidAabProduced: false,
      signedIosIpaProduced: false
    }
  };
}

function parseArguments(argv) {
  const options = {};
  const seen = new Set();
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index];
    const optionName = key === "--app-dir" ? "appDirectory" : key === "--output" ? "output" : null;
    if (!optionName) throw new Error("Unknown argument: " + key);
    if (seen.has(key)) throw new Error("Argument may only be provided once: " + key);

    const value = argv[index + 1];
    if (typeof value !== "string" || value.trim().length === 0 || value.startsWith("--")) {
      throw new Error(key + " requires a value.");
    }

    options[optionName] = value;
    seen.add(key);
    index += 1;
  }
  return options;
}

function main() {
  try {
    const options = parseArguments(process.argv.slice(2));
    if (!options.appDirectory) {
      throw new Error("Usage: node scripts/release-preflight.cjs --app-dir <app-directory> [--output <json-path>]");
    }
    const appDirectory = path.resolve(options.appDirectory);
    const appJson = JSON.parse(fs.readFileSync(path.join(appDirectory, "app.json"), "utf8"));
    const easJson = JSON.parse(fs.readFileSync(path.join(appDirectory, "eas.json"), "utf8"));
    const report = {
      generatedAtUtc: new Date().toISOString(),
      ...validateReleaseConfig({ appJson, easJson, appDirectory })
    };
    const output = JSON.stringify(report, null, 2) + "\n";
    if (options.output) fs.writeFileSync(path.resolve(options.output), output, { encoding: "utf8", flag: "w" });
    else process.stdout.write(output);

    console.log(
      "Release preflight " + report.app.slug + ": engineering checks " +
      report.engineeringChecks.status + "; " + report.externalGates.length +
      " external release gates; production readiness " + report.productionReadiness + "."
    );
    if (report.engineeringChecks.status !== "passed") process.exitCode = 1;
  } catch (error) {
    console.error(error instanceof Error ? error.message : "Release preflight failed.");
    process.exitCode = 1;
  }
}

if (require.main === module) main();
module.exports = { validateReleaseConfig };
