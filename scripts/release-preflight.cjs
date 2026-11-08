"use strict";
const fs = require("node:fs");
const path = require("node:path");

const APP_CONTRACTS = Object.freeze({
  "turnova-rider": Object.freeze({ name: "TurnOva Rider", androidPackage: "com.turnova.rider" }),
  "turnova-driver": Object.freeze({ name: "TurnOva Driver", androidPackage: "com.turnova.driver" })
});
const SEMVER_PATTERN = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$/;
const ANDROID_PACKAGE_PATTERN = /^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+$/;
const IOS_BUNDLE_PATTERN = /^[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+$/;
const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

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

  if (typeof expo.version !== "string" || !SEMVER_PATTERN.test(expo.version)) {
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
  checkAsset("android-adaptive-icon", expo.android?.adaptiveIcon?.foregroundImage);

  const productionApiBaseUrlConfigured = (() => {
    if (typeof apiBaseUrl !== "string" || apiBaseUrl.trim().length === 0) return false;
    try {
      const parsed = new URL(apiBaseUrl);
      const host = parsed.hostname.toLowerCase();
      const ipv4 = host.split(".").map(part => /^\d+$/.test(part) ? Number(part) : NaN);
      const privateIpv4 = ipv4.length === 4 && ipv4.every(part => Number.isInteger(part) && part >= 0 && part <= 255) &&
        (ipv4[0] === 10 || ipv4[0] === 127 ||
          (ipv4[0] === 192 && ipv4[1] === 168) ||
          (ipv4[0] === 172 && ipv4[1] >= 16 && ipv4[1] <= 31) ||
          (ipv4[0] === 169 && ipv4[1] === 254));
      const localHost = host === "localhost" || host.endsWith(".localhost") ||
        host === "10.0.2.2" || host === "0.0.0.0" || host === "[::1]" || privateIpv4;
      return parsed.protocol === "https:" && host.length > 0 && !localHost &&
        !host.includes("<") && !host.includes(">") &&
        parsed.username.length === 0 && parsed.password.length === 0 &&
        parsed.search.length === 0 && parsed.hash.length === 0;
    } catch {
      return false;
    }
  })();
  if (!productionApiBaseUrlConfigured) {
    blocker("production-api-base-url", "Platform / Infrastructure",
      "Provide the approved HTTPS production API base URL as an EAS production environment variable; verify TLS, production CORS origins and health/readiness.");
  }

  const bundleIdentifier = expo.ios?.bundleIdentifier;
  if (typeof bundleIdentifier !== "string" || bundleIdentifier.trim().length === 0) {
    blocker("ios-bundle-identifier", "Product / App Owner",
      "Confirm the final reverse-DNS iOS bundle identifier for this app and register it with Apple.");
  } else if (!IOS_BUNDLE_PATTERN.test(bundleIdentifier) || /^com\.example(?:\.|$)/i.test(bundleIdentifier)) {
    error("ios-bundle-identifier", "The configured iOS bundle identifier is malformed or a placeholder.");
  }

  const projectId = expo.extra?.eas?.projectId;
  if (typeof projectId !== "string" || projectId.trim().length === 0) {
    blocker("eas-project-id", "Release Engineering",
      "Link this app to the approved EAS project and add its project ID to the app configuration.");
  } else if (!UUID_PATTERN.test(projectId)) {
    error("eas-project-id", "expo.extra.eas.projectId must be a valid UUID.");
  }

  const cliVersion = easJson?.cli?.version;
  if (typeof cliVersion !== "string" || !/^>=\s*16(?:\.|$)/.test(cliVersion)) {
    error("eas-cli-version", "eas.json must require EAS CLI 16 or newer.");
  }

  const profiles = easJson?.build || {};
  const development = profiles.development;
  const preview = profiles.preview;
  const production = profiles.production;
  if (development?.developmentClient !== true || development?.distribution !== "internal") {
    error("development-profile", "The development profile must be an internal development-client build.");
  }
  if (preview?.distribution !== "internal" || preview?.android?.buildType !== "apk") {
    error("preview-profile", "The preview profile must produce an internally distributed Android APK.");
  }
  if (production?.android?.buildType !== "app-bundle" || production?.ios?.distribution !== "store") {
    error("production-profile", "The production profile must target an Android app bundle and iOS store distribution.");
  }
  if (production && production.autoIncrement !== true) {
    blocker("build-number-increment", "Release Engineering",
      "Approve and configure the EAS production build-number source and automatic increment policy before repeated store uploads.");
  }

  blocker("signing-credentials", "Release Owner",
    "Verify Android upload signing and Apple distribution certificate/provisioning credentials through EAS secure credentials; never commit them.");
  blocker("physical-device-acceptance", "QA / Product Owner",
    "Install signed Rider and Driver builds on the agreed Android/iOS device matrix and record functional, security and pilot acceptance.");
  blocker("store-submission", "Release Owner",
    "Complete store metadata, privacy disclosures, review submission and approval for both apps.");

  const engineeringPassed = errors.length === 0;
  const configurationBlockerIds = new Set(["production-api-base-url", "ios-bundle-identifier", "eas-project-id", "build-number-increment"]);
  const productionBuildConfigurationComplete =
    engineeringPassed && !externalGates.some(item => configurationBlockerIds.has(item.id));

  return {
    schemaVersion: 1,
    app: {
      slug: slug || null,
      name: typeof expo.name === "string" ? expo.name : null,
      version: typeof expo.version === "string" ? expo.version : null,
      androidPackage: typeof expo.android?.package === "string" ? expo.android.package : null,
      iosBundleIdentifier: typeof bundleIdentifier === "string" ? bundleIdentifier : null,
      easProjectLinked: typeof projectId === "string" && UUID_PATTERN.test(projectId),
      productionApiBaseUrlConfigured
    },
    engineeringChecks: { status: engineeringPassed ? "passed" : "failed", errors },
    productionBuildConfigurationComplete,
    productionReadiness: externalGates.length === 0 && engineeringPassed ? "not_verified" : "blocked",
    externalGates,
    currentCiArtifactEvidence: {
      android: "debug-apk-only",
      ios: "unsigned-ios-simulator-app-only",
      signedAndroidAabProduced: false,
      signedIosIpaProduced: false
    }
  };
}

function parseArguments(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const key = argv[index];
    if (key === "--app-dir") options.appDirectory = argv[++index];
    else if (key === "--output") options.output = argv[++index];
    else throw new Error("Unknown argument: " + key);
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
