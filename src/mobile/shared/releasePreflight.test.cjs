"use strict";
const assert = require("node:assert/strict");
const path = require("node:path");
const test = require("node:test");
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
