# TurnOva production app icons

This directory contains the selected Multi-Mobility app-icon artwork.

- `turnova-app-icon.svg`: full-bleed square icon artwork (navy background).
- `turnova-adaptive-foreground.svg`: transparent foreground artwork for Android adaptive icons.
- `icon.png`: generated 1024×1024 PNG used by iOS/App Store and Expo.
- `adaptive-icon.png`: generated 432×432 transparent foreground used by Android adaptive icons.

Generate the PNG files after installing dependencies:

```bash
npm install
npm run generate-icons
```

Android uses the native adaptive icon background color `#061D3D`; keep the foreground artwork inside the central safe area because launchers apply different masks. iOS uses a full-bleed 1024×1024 icon; do not add rounded corners because iOS applies its own mask.

The SVG sources are the source of truth. Re-run the generator after changing the artwork.
