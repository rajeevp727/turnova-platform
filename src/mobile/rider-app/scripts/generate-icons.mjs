import sharp from "sharp";
import { mkdir } from "node:fs/promises";

const assets = new URL("../assets/", import.meta.url);
await mkdir(assets, { recursive: true });
const master = new URL("../assets/turnova-app-icon.svg", import.meta.url);
const foreground = new URL("../assets/turnova-adaptive-foreground.svg", import.meta.url);

// Store icon: 1024x1024 PNG with a full-bleed background.
await sharp(master).resize(1024, 1024).png().toFile(new URL("icon.png", assets));
// Android adaptive foreground: transparent canvas with generous safe-zone padding.
await sharp(foreground).resize(432, 432, { fit: "contain", background: { r: 0, g: 0, b: 0, alpha: 0 } })
  .extend({ top: 0, bottom: 0, left: 0, right: 0, background: { r: 0, g: 0, b: 0, alpha: 0 } })
  .png().toFile(new URL("adaptive-icon.png", assets));
console.log("Generated iOS/Expo icon.png (1024px) and Android adaptive-icon.png (432px).");
