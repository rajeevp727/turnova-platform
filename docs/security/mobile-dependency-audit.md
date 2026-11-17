# Mobile dependency audit scope assessment

**Assessment date:** 2026-11-18  
**Apps:** TurnOva Rider and TurnOva Driver  
**Platform phase:** Android  
**Current dependency baseline:** Expo SDK 54 / React Native 0.81.4

## Current exact audit evidence

The PR #185 run [#44158843845](https://github.com/rajeevp727/turnova-platform/actions/runs/44158843845) generated fresh lockfiles and completed npm ci for both apps. Both audit artifacts report:

| App | Total package findings | High package nodes | Moderate | Critical |
|---|---:|---:|---:|---:|
| Rider | 34 | 27 | 7 | 0 |
| Driver | 34 | 27 | 7 | 0 |

The 27 high package nodes are a transitive cascade from one direct high advisory root: [GHSA-fc5q-39xc-vjpf](https://github.com/advisories/GHSA-fc5q-39xc-vjpf), affecting picomatch through 4.0.7. The lockfiles resolve picomatch 2.3.2 and 4.0.7 under Expo CLI, Expo Metro config, tinyglobby, anymatch, Jest and micromatch tooling. It is not a direct Rider or Driver dependency.

The GitHub advisory currently has no patched version listed. The advisory is disputed by the package maintainer; Snyk's [amendment](https://security.snyk.io/vuln/SNYK-JS-PICOMATCH-21248802) records that it was deemed not a vulnerability. TurnOva does not treat that dispute as a patch or erase the finding.

The same audit reports seven moderate package nodes rooted in glob and minimatch advisories. They remain recorded for follow-up; they are not represented as fixed.

## TurnOva exposure assessment and CI controls

The affected picomatch copies are build/test tooling, not app source dependencies. The current apps do not import picomatch directly and do not accept user-supplied glob patterns. The Android validation job now performs all of these checks before it can pass:

1. Fail on any critical finding or npm audit execution error.
2. Resolve every high package node through the npm audit dependency graph; the only accepted high root must be the exact picomatch GHSA above.
3. Require picomatch to remain transitive, with no direct app dependency.
4. Require every picomatch dependency parent to remain within the reviewed Expo CLI/Metro/Jest build-tool set.
5. Search app source for direct picomatch imports.
6. Export the Android production bundle with source maps and fail if any source map contains picomatch code.
7. Remove verification-only source maps before uploading the production bundle artifact.
8. Fail on any new or unknown high advisory, any direct app use, any unreviewed parent, or any bundle inclusion.

The exception is deliberately narrow and fail-closed. It is a contextual build-tooling risk assessment, not a patched dependency and not a claim that npm audit is clean. The source-map check is required for each app and each exact CI head.

## Upgrade decision

No blind Expo/React Native major upgrade is included in this dependency-reproducibility PR. Expo SDK 58 upgrades React Native to 0.88 and documents TypeScript/native lifecycle and Android release-build changes; such a migration needs its own Rider/Driver regression work and platform evidence. Expo's [SDK 58 upgrade guide](https://expo.dev/blog/how-to-upgrade-to-sdk-58) and [SDK 58 release notes](https://expo.dev/changelog/sdk-58) are the compatibility references.

Revisit the picomatch advisory when the maintainer publishes a patched release or the advisory is withdrawn. Until then, preserve the explicit CI exception and the bundle-exclusion proof; do not claim the advisory is fixed.
