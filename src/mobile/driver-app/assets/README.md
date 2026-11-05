# TurnOva production app icons

This directory contains the source artwork for the selected Multi-Mobility brand.

- `turnova-app-icon.svg`: full-bleed square artwork for iOS and Expo.
- `turnova-adaptive-foreground.svg`: transparent artwork scaled into Android's adaptive-icon safe zone.
- `icon.png`: generated 1024×1024 opaque PNG for iOS/App Store and Expo.
- `adaptive-icon.png`: generated 432×432 transparent PNG for Android adaptive icons.

The PNGs are generated automatically by the package `postinstall` hook (and can be regenerated manually with `npm run generate-icons`). Sharp is a runtime build dependency so clean EAS build environments can generate the files before Expo resolves the app config.

Android uses native adaptive-icon background color `#061D3D`; launchers apply different masks. iOS artwork is full-bleed square with no pre-rounded corners because iOS applies its own mask.

## EAS builds

Run these from this app directory:

```bash
npx eas build --platform android --profile preview
npx eas build --platform android --profile production
npx eas build --platform ios --profile production
```
