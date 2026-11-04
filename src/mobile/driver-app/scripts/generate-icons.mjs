import sharp from "sharp";
import { mkdir } from "node:fs/promises";

const assets = new URL("../assets/", import.meta.url);
await mkdir(assets, { recursive: true });
const master = new URL("../assets/turnova-app-icon.svg", import.meta.url);
const foreground = new URL("../assets/turnova-adaptive-foreground.svg", import.meta.url);

const iconPath = new URL("icon.png", assets);
const adaptivePath = new URL("adaptive-icon.png", assets);

// iOS and Expo require a square, opaque 1024px icon. iOS applies its own corner mask.
await sharp(master).resize(1024, 1024, { fit: "cover" }).png().toFile(iconPath);

// Android adaptive foreground is transparent and artwork is scaled into the launcher safe zone.
await sharp(foreground).resize(432, 432, { fit: "contain", background: { r: 0, g: 0, b: 0, alpha: 0 } })
  .png().toFile(adaptivePath);

const [icon, adaptive] = await Promise.all([sharp(iconPath).metadata(), sharp(adaptivePath).metadata()]);
if (icon.width !== 1024 || icon.height !== 1024 || icon.hasAlpha) {
  throw new Error("App icon must be an opaque 1024x1024 PNG.");
}
if (adaptive.width !== 432 || adaptive.height !== 432 || !adaptive.hasAlpha) {
  throw new Error("Android adaptive foreground must be a transparent 432x432 PNG.");
}
console.log("Validated TurnOva icons: iOS 1024x1024; Android adaptive foreground 432x432 with alpha.");
