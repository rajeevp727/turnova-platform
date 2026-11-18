# TurnOva brand assets

## Selected direction: Multi-Mobility (Concept D)

- `turnova-multi-mobility.svg` — scalable master artwork for the selected direction.
- `src/mobile/shared/TurnOvaLogo.tsx` — reusable React Native logo lockup used by the Rider and Driver apps.
- Driver monthly payslips use a TurnOva mark beside the company name and a low-contrast, page-level brand watermark.

### Brand language

- Blue: four-wheel mobility.
- Teal: two-wheel mobility.
- Deep navy: trust and technology.
- Road motif: movement and connectivity.

### Production notes

The shared mobile component renders the mark as vector artwork using `react-native-svg`. Both mobile apps declare that dependency. The PDF uses vector drawing commands so the branding remains crisp when printed or zoomed.

The selected concept is now applied to the Rider and Driver app headers and the monthly driver payslip PDF. Store-ready Android adaptive icon layers and iOS launcher icon exports remain a separate packaging task; the current wordmark lockup is intended for in-app headers, not as a launcher icon.
