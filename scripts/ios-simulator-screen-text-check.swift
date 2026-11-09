import AppKit
import Darwin
import Foundation
import Vision

let arguments = Array(CommandLine.arguments.dropFirst())
let expectsSecureStoreUnavailable = arguments.contains("--expect-secure-store-unavailable")
let screenshotArguments = arguments.filter { $0 != "--expect-secure-store-unavailable" }
guard screenshotArguments.count == 1 else {
    fputs("Usage: ios-simulator-screen-text-check.swift <screenshot.png> [--expect-secure-store-unavailable]\n", stderr)
    exit(2)
}

let imageURL = URL(fileURLWithPath: screenshotArguments[0])
let request = VNRecognizeTextRequest()
request.recognitionLevel = .accurate
request.recognitionLanguages = ["en-US"]
request.usesLanguageCorrection = false

do {
    try VNImageRequestHandler(url: imageURL, options: [:]).perform([request])
} catch {
    fputs("Unable to recognize iOS Simulator screenshot text: \(error)\n", stderr)
    exit(1)
}

let recognizedLines = (request.results ?? []).compactMap {
    $0.topCandidates(1).first?.string
}
let recognizedText = recognizedLines.joined(separator: "\n")
// Vision may return one visual banner as multiple OCR lines. Collapse all whitespace
// before checking forbidden phrases so a line wrap cannot bypass the stale-session guard.
let normalizedRecognizedText = recognizedText
    .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
    .trimmingCharacters(in: .whitespacesAndNewlines)
let requiredText = ["Welcome back", "Email address", "Password", "Sign in"]
let missingText = requiredText.filter {
    !normalizedRecognizedText.localizedCaseInsensitiveContains($0)
}
let secureStoreUnavailableMarker = "Unable to restore your session"
let secureStoreUnavailableDetail = "Check your connection or secure storage and retry"
let secureStoreUnavailableVisible =
    normalizedRecognizedText.localizedCaseInsensitiveContains(secureStoreUnavailableMarker) &&
    normalizedRecognizedText.localizedCaseInsensitiveContains(secureStoreUnavailableDetail)
if expectsSecureStoreUnavailable && !secureStoreUnavailableVisible {
    fputs("Expected unsigned-simulator SecureStore failure banner was not visible.\n", stderr)
    exit(1)
}

var forbiddenText = [
    "Unable to restore session",
    "Session expired",
    "Unable to clear secure session storage",
    "Unable to securely save session tokens"
]
if !expectsSecureStoreUnavailable {
    forbiddenText.append(secureStoreUnavailableMarker)
}
let unexpectedText = forbiddenText.filter {
    normalizedRecognizedText.localizedCaseInsensitiveContains($0)
}

print(recognizedText)
if !missingText.isEmpty {
    fputs("iOS login screen is missing expected text: \(missingText.joined(separator: ", "))\n", stderr)
    exit(1)
}
if !unexpectedText.isEmpty {
    fputs("iOS fresh-install login screen contains an unexpected session/storage error: \(unexpectedText.joined(separator: ", "))\n", stderr)
    exit(1)
}
if expectsSecureStoreUnavailable {
    print("Unsigned simulator SecureStore-unavailable banner verified; Keychain success is not claimed.")
}
