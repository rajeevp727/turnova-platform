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
      ios: { supportsTablet: true },
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
    exists: candidate => available.has(candidate),
    available
  };
}

test("current Rider config passes structural checks but reports unprovided release inputs", () => {
  const report = validateReleaseConfig(fixture());
  assert.equal(report.app.slug, "turnova-rider");
  assert.equal(report.engineeringChecks.status, "passed");
  assert.equal(report.productionBuildConfigurationComplete, false);
  assert.equal(report.productionReadiness, "blocked");
  assert.deepEqual(report.externalGates.map(item => item.id).sort(), [
    "build-number-increment", "eas-project-id", "ios-bundle-identifier",
    "physical-device-acceptance", "signing-credentials", "store-submission"
  ].sort());
  assert.equal(report.currentCiArtifactEvidence.android, "debug-apk-only");
  assert.equal(report.currentCiArtifactEvidence.ios, "unsigned-ios-simulator-app-only");
  assert.equal(report.currentCiArtifactEvidence.signedAndroidAabProduced, false);
  assert.equal(report.currentCiArtifactEvidence.signedIosIpaProduced, false);
});

test("valid release identifiers and increment policy clear only configuration gates", () => {
  const input = fixture({
    appJson: {
      ios: { supportsTablet: true, bundleIdentifier: "com.turnova.rider.ios" },
      extra: { eas: { projectId: "123e4567-e89b-12d3-a456-426614174000" } }
    },
    easJson: {
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
  assert.equal(report.productionBuildConfigurationComplete, true);
  assert.deepEqual(report.externalGates.map(item => item.id).sort(), [
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

test("malformed iOS bundle identifier and EAS project ID fail instead of becoming external blockers", () => {
  const report = validateReleaseConfig(fixture({
    appJson: {
      ios: { supportsTablet: true, bundleIdentifier: "com.example.placeholder" },
      extra: { eas: { projectId: "not-a-uuid" } }
    }
  }));
  const ids = report.engineeringChecks.errors.map(item => item.id);
  assert.ok(ids.includes("ios-bundle-identifier"));
  assert.ok(ids.includes("eas-project-id"));
  assert.ok(!report.externalGates.some(item => item.id === "ios-bundle-identifier"));
  assert.ok(!report.externalGates.some(item => item.id === "eas-project-id"));
});
