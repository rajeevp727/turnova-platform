import sharp from "sharp";
import { mkdir } from "node:fs/promises";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const assets = fileURLToPath(new URL("../assets/", import.meta.url));
await mkdir(assets, { recursive: true });
const master = join(assets, "turnova-app-icon.svg");
const foreground = join(assets, "turnova-adaptive-foreground.svg");
const iconPath = join(assets, "icon.png");
const adaptivePath = join(assets, "adaptive-icon.png");

// iOS and Expo require a square, opaque 1024px icon. iOS applies its own corner mask.
await sharp(master).resize(1024, 1024, { fit: "cover" }).png().toFile(iconPath);

// Android adaptive foreground is transparent and artwork is scaled into the launcher safe zone.
await sharp(foreground).resize(432, 432, { fit: "contain", background: { r: 0, g: 0, b: 0, alpha: 0 } })
  .png().toFile(adaptivePath);

const [icon, adaptive] = await Promise.all([sharp(iconPath).metadata(), sharp(adaptivePath).metadata()]);
if (icon.width !== 1024 || icon.height !== 1024) {
  throw new Error("App icon must be a 1024x1024 PNG.");
}
if (adaptive.width !== 432 || adaptive.height !== 432 || !adaptive.hasAlpha) {
  throw new Error("Android adaptive foreground must be a transparent 432x432 PNG.");
}
const [iconPixels, adaptivePixels] = await Promise.all([
  sharp(iconPath).ensureAlpha().raw().toBuffer(),
  sharp(adaptivePath).ensureAlpha().raw().toBuffer()
]);
if (iconPixels[3] !== 255) throw new Error("iOS app icon must be fully opaque.");
if (adaptivePixels[3] !== 0) throw new Error("Android adaptive foreground must preserve transparency.");
console.log("Validated TurnOva icons: iOS 1024x1024 opaque; Android adaptive foreground 432x432 transparent.");
