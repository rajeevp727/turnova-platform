const path = require("path");
const { getDefaultConfig } = require("expo/metro-config");

const config = getDefaultConfig(__dirname);

// App source imports shared refresh/recovery modules from src/mobile/shared.
config.watchFolders = [path.resolve(__dirname, "../shared")];

module.exports = config;
