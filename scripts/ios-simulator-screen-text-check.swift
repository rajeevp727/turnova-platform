import AppKit
import Darwin
import Foundation
import Vision

guard CommandLine.arguments.count == 2 else {
    fputs("Usage: ios-simulator-screen-text-check.swift <screenshot.png>\n", stderr)
    exit(2)
}

let imageURL = URL(fileURLWithPath: CommandLine.arguments[1])
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
let forbiddenText = [
    "Unable to restore your session",
    "Unable to restore session",
    "Session expired",
    "Unable to clear secure session storage"
]
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
