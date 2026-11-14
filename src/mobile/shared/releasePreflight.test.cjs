"use strict";
const assert = require("node:assert/strict");
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const test = require("node:test");
const RELEASE_PREFLIGHT_SCRIPT = path.resolve(__dirname, "../../../scripts/release-preflight.cjs");
const { validateReleaseConfig } = require("../../../scripts/release-preflight.cjs");

function fixture(overrides = {}) {
  const appDirectory = path.resolve("/fixture/turnova-rider");
  const appJson = {
    expo: {
      name: "TurnOva Rider",
      slug: "turnova-rider",
      version: "0.1.0",
      icon: "./assets/icon.png",
      ios: { supportsTablet: true, icon: "./assets/icon.png" },
      android: {
        package: "com.turnova.rider",
        adaptiveIcon: { foregroundImage: "./assets/adaptive-icon.png", backgroundColor: "#061D3D" }
      }
    }
  };
  const easJson = {
    cli: { version: ">= 16.0.0" },
    build: {
      development: { developmentClient: true, distribution: "internal" },
      preview: { distribution: "internal", android: { buildType: "apk" } },
      production: { android: { buildType: "app-bundle" }, ios: { distribution: "store" } }
    }
  };

  if (overrides.appJson) Object.assign(appJson.expo, overrides.appJson);
  if (overrides.easJson?.cli) easJson.cli = { ...easJson.cli, ...overrides.easJson.cli };
  if (overrides.easJson?.build) easJson.build = { ...easJson.build, ...overrides.easJson.build };

  const available = new Set([
    path.resolve(appDirectory, "assets/icon.png"),
    path.resolve(appDirectory, "assets/adaptive-icon.png")
  ]);
  return {
    appJson,
    easJson,
    appDirectory,
    apiBaseUrl: Object.prototype.hasOwnProperty.call(overrides, "apiBaseUrl") ? overrides.apiBaseUrl : null,
    exists: candidate => available.has(candidate),
    available
  };
}

test("current Rider config passes structural checks but reports unprovided release inputs", () => {
  const report = validateReleaseConfig(fixture());
  assert.equal(report.schemaVersion, 2);
  assert.equal(report.app.slug, "turnova-rider");
  assert.equal(report.app.productionApiBaseUrlConfiguredInCi, false);
  assert.equal(report.engineeringChecks.status, "passed");
  assert.equal(report.ciVisibleReleaseConfigurationComplete, false);
  assert.equal(report.productionReadiness, "blocked");
  assert.deepEqual(report.externalGates.map(item => item.id).sort(), [
    "build-number-policy", "eas-project-linkage", "ios-bundle-registration", "production-api-base-url",
    "physical-device-acceptance", "signing-credentials", "store-submission"
  ].sort());
  assert.equal(report.currentCiArtifactEvidence.android, "debug-apk-and-standalone-release-apk-variant");
  assert.equal(report.currentCiArtifactEvidence.ios, "unsigned-ios-simulator-app-only");
  assert.equal(report.currentCiArtifactEvidence.signedAndroidAabProduced, false);
  assert.equal(report.currentCiArtifactEvidence.signedIosIpaProduced, false);
});

test("valid local release configuration passes syntax while external verification gates remain", () => {
  const input = fixture({
    apiBaseUrl: "https://api.turnova.com",
    appJson: {
      ios: { supportsTablet: true, bundleIdentifier: "com.turnova.rider.ios" },
      extra: { eas: { projectId: "123e4567-e89b-12d3-a456-426614174000" } }
    },
    easJson: {
      cli: { appVersionSource: "remote" },
      build: {
        production: {
          android: { buildType: "app-bundle" },
          ios: { distribution: "store" },
          autoIncrement: true
        }
      }
    }
  });
  const report = validateReleaseConfig(input);
  assert.equal(report.engineeringChecks.status, "passed");
  assert.equal(report.ciVisibleReleaseConfigurationComplete, true);
  assert.equal(report.app.productionApiBaseUrlConfiguredInCi, true);
  assert.equal(report.app.iosBundleIdentifierSyntaxValid, true);
  assert.equal(report.app.easProjectIdSyntaxValid, true);
  assert.deepEqual(report.externalGates.map(item => item.id).sort(), [
    "build-number-policy", "eas-project-linkage", "ios-bundle-registration", "production-api-base-url",
    "physical-device-acceptance", "signing-credentials", "store-submission"
  ].sort());
  assert.equal(report.productionReadiness, "blocked");
});

test("wrong Android identity, missing generated icon and invalid production profile fail preflight", () => {
  const input = fixture({
    appJson: { android: { package: "com.example.rider" } },
    easJson: {
      build: {
        production: { android: { buildType: "apk" }, ios: { distribution: "internal" } }
      }
    }
  });
  input.available.delete(path.resolve(input.appDirectory, "assets/adaptive-icon.png"));
  const report = validateReleaseConfig(input);
  const ids = report.engineeringChecks.errors.map(item => item.id);
  assert.equal(report.engineeringChecks.status, "failed");
  assert.ok(ids.includes("android-package"));
  assert.ok(ids.includes("android-adaptive-icon"));
  assert.ok(ids.includes("production-profile"));
});

test("icon paths cannot escape the app directory", () => {
  const report = validateReleaseConfig(fixture({ appJson: { icon: "../private/icon.png" } }));
  assert.equal(report.engineeringChecks.status, "failed");
  assert.ok(report.engineeringChecks.errors.some(item => item.id === "app-icon"));
});

test("placeholder iOS bundle identifiers and malformed EAS project IDs fail instead of becoming external blockers", () => {
  for (const bundleIdentifier of ["com.example", "com.example.placeholder"]) {
    const report = validateReleaseConfig(fixture({
      appJson: {
        ios: { supportsTablet: true, bundleIdentifier },
        extra: { eas: { projectId: "not-a-uuid" } }
      }
    }));
    const ids = report.engineeringChecks.errors.map(item => item.id);
    assert.ok(ids.includes("ios-bundle-identifier"), bundleIdentifier);
    assert.ok(ids.includes("eas-project-id"), bundleIdentifier);
    assert.ok(!report.externalGates.some(item => item.id === "ios-bundle-registration"));
    assert.ok(!report.externalGates.some(item => item.id === "eas-project-linkage"));
  }
});

test("missing or non-HTTPS production API endpoints remain explicit release gates", () => {
  const missing = validateReleaseConfig(fixture());
  const emulator = validateReleaseConfig(fixture({ apiBaseUrl: "http://10.0.2.2:5000" }));
  const placeholder = validateReleaseConfig(fixture({ apiBaseUrl: "https://<TURNOVA_API_HOST>" }));
  const loopback = validateReleaseConfig(fixture({ apiBaseUrl: "https://localhost:5000" }));
  const queryCredential = validateReleaseConfig(fixture({ apiBaseUrl: "https://api.turnova.example.com?token=unsafe" }));

  for (const report of [missing, emulator, placeholder, loopback, queryCredential]) {
    assert.equal(report.app.productionApiBaseUrlConfiguredInCi, false);
    assert.ok(report.externalGates.some(item => item.id === "production-api-base-url"));
    assert.equal(report.ciVisibleReleaseConfigurationComplete, false);
  }
});

test("Driver app identity and established Android package pass the same preflight contract", () => {
  const input = fixture();
  input.appDirectory = path.resolve("/fixture/turnova-driver");
  input.appJson.expo.slug = "turnova-driver";
  input.appJson.expo.name = "TurnOva Driver";
  input.appJson.expo.android.package = "com.turnova.driver";
  input.available.clear();
  input.available.add(path.resolve(input.appDirectory, "assets/icon.png"));
  input.available.add(path.resolve(input.appDirectory, "assets/adaptive-icon.png"));
  const report = validateReleaseConfig(input);
  assert.equal(report.app.slug, "turnova-driver");
  assert.equal(report.app.androidPackage, "com.turnova.driver");
  assert.equal(report.engineeringChecks.status, "passed");
});

test("private, reserved and internal API hosts fail while public IPv4 and IPv6 pass syntax validation", () => {
  const rejectedHosts = [
    "https://100.64.0.1", "https://192.0.2.1", "https://198.18.0.1", "https://203.0.113.10",
    "https://[fc00::1]", "https://[fe80::1]", "https://[2001:db8::1]", "https://[2001:20::1]",
    "https://[3fff::1]", "https://[2002:7f00:1::]", "https://[::ffff:192.168.1.10]",
    "https://api.local", "https://api.internal", "https://api.turnova.example.com", "https://example.com", "https://api.example.net", "https://api", "https://localhost"
  ];
  for (const apiBaseUrl of rejectedHosts) {
    const report = validateReleaseConfig(fixture({ apiBaseUrl }));
    assert.equal(report.app.productionApiBaseUrlConfiguredInCi, false, apiBaseUrl);
  }
  for (const apiBaseUrl of ["https://8.8.8.8", "https://[2606:4700:4700::1111]", "https://api.turnova.com"]) {
    const report = validateReleaseConfig(fixture({ apiBaseUrl }));
    assert.equal(report.app.productionApiBaseUrlConfiguredInCi, true, apiBaseUrl);
    assert.ok(report.externalGates.some(item => item.id === "production-api-base-url"));
  }
});

test("build number policy requires an explicit local or remote source and production auto increment", () => {
  const autoIncrementWithoutSource = validateReleaseConfig(fixture({
    easJson: { build: { production: { autoIncrement: true } } }
  }));
  assert.equal(autoIncrementWithoutSource.ciVisibleReleaseConfigurationComplete, false);
  assert.ok(autoIncrementWithoutSource.externalGates.some(item => item.id === "build-number-policy"));
  const invalidSource = validateReleaseConfig(fixture({
    easJson: { cli: { appVersionSource: "automatic" } }
  }));
  assert.equal(invalidSource.engineeringChecks.status, "failed");
  assert.ok(invalidSource.engineeringChecks.errors.some(item => item.id === "build-number-source"));
});

test("configured iOS icon path is checked independently from the general Expo icon", () => {
  const report = validateReleaseConfig(fixture({
    appJson: { ios: { supportsTablet: true, icon: "./assets/missing-ios-icon.png" } }
  }));
  assert.equal(report.engineeringChecks.status, "failed");
  assert.ok(report.engineeringChecks.errors.some(item => item.id === "ios-icon"));
});


test("Expo app version enforces SemVer 2.0.0 core and identifier rules", () => {
  const validVersions = [
    "0.0.0",
    "1.2.3",
    "1.2.3-alpha",
    "1.2.3-alpha.1",
    "1.2.3-0",
    "1.2.3+build.007",
    "1.2.3-rc.1+build.7"
  ];
  for (const version of validVersions) {
    const report = validateReleaseConfig(fixture({ appJson: { version } }));
    assert.ok(!report.engineeringChecks.errors.some(item => item.id === "app-version"), version);
  }

  const invalidVersions = [
    "01.2.3", "1.02.3", "1.2.03", "1.2.3-01", "1.2.3-00",
    "1.2.3-alpha..1", "1.2.3+build..1", "1.2.3-", "1.2.3+", "1.2.3\n"
  ];
  for (const version of invalidVersions) {
    const report = validateReleaseConfig(fixture({ appJson: { version } }));
    assert.ok(report.engineeringChecks.errors.some(item => item.id === "app-version"), version);
  }
});

test("EAS CLI minimum accepts only a strict >= SemVer range at version 16 or newer", () => {
  for (const version of [">= 16.0.0", ">=16.0.1", ">= 16.10.0", ">= 17.0.0"]) {
    const report = validateReleaseConfig(fixture({ easJson: { cli: { version } } }));
    assert.ok(!report.engineeringChecks.errors.some(item => item.id === "eas-cli-version"), version);
  }

  for (const version of [
    ">= 15.9.9", ">= 16", ">= 16.0", ">= 016.0.0",
    ">= 16.0.0-beta.1", ">= 16.0.0junk", ">= 16.0.0\n", ">= 16.0.0 || >= 17.0.0", "^16.0.0"
  ]) {
    const report = validateReleaseConfig(fixture({ easJson: { cli: { version } } }));
    assert.ok(report.engineeringChecks.errors.some(item => item.id === "eas-cli-version"), version);
  }
});

test("development, preview and production EAS profiles fail closed at their contract boundaries", () => {
  const scenarios = [
    { id: "development-profile", build: { development: { developmentClient: false, distribution: "internal" } } },
    { id: "development-profile", build: { development: { developmentClient: true, distribution: "store" } } },
    { id: "preview-profile", build: { preview: { distribution: "store", android: { buildType: "apk" } } } },
    { id: "preview-profile", build: { preview: { distribution: "internal", android: { buildType: "app-bundle" } } } },
    { id: "production-profile", build: { production: { android: { buildType: "apk" }, ios: { distribution: "store" } } } },
    { id: "production-profile", build: { production: { android: { buildType: "app-bundle" }, ios: { distribution: "internal" } } } }
  ];

  for (const scenario of scenarios) {
    const report = validateReleaseConfig(fixture({ easJson: { build: scenario.build } }));
    assert.ok(report.engineeringChecks.errors.some(item => item.id === scenario.id), JSON.stringify(scenario));
  }
});

test("production API URL rejects credentials, query strings and fragments", () => {
  for (const apiBaseUrl of [
    "https://user:secret@api.turnova.com",
    "https://api.turnova.com?token=unsafe",
    "https://api.turnova.com#release",
    "https://user@api.turnova.com"
  ]) {
    const report = validateReleaseConfig(fixture({ apiBaseUrl }));
    assert.equal(report.app.productionApiBaseUrlConfiguredInCi, false, apiBaseUrl);
    assert.ok(report.externalGates.some(item => item.id === "production-api-base-url"));
  }
});

test("production API URL rejects reserved address and internal hostname boundaries", () => {
  const rejectedHosts = [
    "https://0.0.0.1", "https://127.0.0.1", "https://169.254.169.254",
    "https://172.16.0.1", "https://172.31.255.255", "https://192.168.1.1",
    "https://224.0.0.1", "https://240.0.0.1", "https://255.255.255.255",
    "https://[::]", "https://[::1]", "https://[ff02::1]", "https://[2002::1]",
    "https://api.localdomain", "https://api.lan", "https://api.home",
    "https://api.home.arpa", "https://api.test", "https://api.invalid",
    "https://api.intranet", "https://api.private", "https://api.onion",
    "https://API.LOCAL."
  ];
  for (const apiBaseUrl of rejectedHosts) {
    const report = validateReleaseConfig(fixture({ apiBaseUrl }));
    assert.equal(report.app.productionApiBaseUrlConfiguredInCi, false, apiBaseUrl);
  }

  const acceptedHosts = [
    "https://172.15.255.255",
    "https://172.32.0.1",
    "https://198.20.0.1",
    "https://[2001:4860:4860::8888]",
    "https://api.turnova.com"
  ];
  for (const apiBaseUrl of acceptedHosts) {
    const report = validateReleaseConfig(fixture({ apiBaseUrl }));
    assert.equal(report.app.productionApiBaseUrlConfiguredInCi, true, apiBaseUrl);
  }
});

function createCliFixture(root, version = "0.1.0") {
  const appDirectory = path.join(root, "app");
  const assetsDirectory = path.join(appDirectory, "assets");
  fs.mkdirSync(assetsDirectory, { recursive: true });
  const input = fixture({ appJson: { version } });
  fs.writeFileSync(path.join(appDirectory, "app.json"), JSON.stringify(input.appJson), "utf8");
  fs.writeFileSync(path.join(appDirectory, "eas.json"), JSON.stringify(input.easJson), "utf8");
  fs.writeFileSync(path.join(assetsDirectory, "icon.png"), "fixture", "utf8");
  fs.writeFileSync(path.join(assetsDirectory, "adaptive-icon.png"), "fixture", "utf8");
  return appDirectory;
}

function runReleasePreflightCli(args) {
  return spawnSync(process.execPath, [RELEASE_PREFLIGHT_SCRIPT, ...args], {
    encoding: "utf8",
    env: { ...process.env, EXPO_PUBLIC_API_BASE_URL: "" }
  });
}

test("release preflight CLI writes a machine-readable report and summarizes external gates", t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "turnova-release-preflight-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const appDirectory = createCliFixture(root);
  const outputPath = path.join(root, "release-readiness.json");
  const result = runReleasePreflightCli(["--app-dir", appDirectory, "--output", outputPath]);

  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stderr, "");
  assert.match(result.stdout, /Release preflight turnova-rider: engineering checks passed; 7 external release gates; production readiness blocked\./);
  const report = JSON.parse(fs.readFileSync(outputPath, "utf8"));
  assert.equal(report.engineeringChecks.status, "passed");
  assert.equal(report.productionReadiness, "blocked");
  assert.equal(report.externalGates.length, 7);
  assert.ok(Number.isFinite(Date.parse(report.generatedAtUtc)));
});

test("release preflight CLI writes a failed report and exits non-zero for invalid app metadata", t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "turnova-release-preflight-invalid-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const appDirectory = createCliFixture(root, "01.2.3");
  const outputPath = path.join(root, "release-readiness.json");
  const result = runReleasePreflightCli(["--app-dir", appDirectory, "--output", outputPath]);

  assert.equal(result.status, 1, result.stderr);
  const report = JSON.parse(fs.readFileSync(outputPath, "utf8"));
  assert.equal(report.engineeringChecks.status, "failed");
  assert.ok(report.engineeringChecks.errors.some(item => item.id === "app-version"));
  assert.match(result.stdout, /engineering checks failed/);
});

test("release preflight CLI rejects missing, duplicate and unknown arguments", () => {
  const scenarios = [
    { args: [], message: "Usage: node scripts/release-preflight.cjs" },
    { args: ["--app-dir"], message: "--app-dir requires a value." },
    { args: ["--app-dir", "--output"], message: "--app-dir requires a value." },
    { args: ["--output", "report.json"], message: "Usage: node scripts/release-preflight.cjs" },
    { args: ["--unexpected"], message: "Unknown argument: --unexpected" },
    { args: ["--app-dir", "/fixture", "--app-dir", "/fixture"], message: "Argument may only be provided once: --app-dir" },
    { args: ["--app-dir", "/fixture", "--output"], message: "--output requires a value." }
  ];

  for (const scenario of scenarios) {
    const result = runReleasePreflightCli(scenario.args);
    assert.equal(result.status, 1, JSON.stringify(scenario));
    assert.ok(result.stderr.includes(scenario.message), result.stderr);
  }
});
