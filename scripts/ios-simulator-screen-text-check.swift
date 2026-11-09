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
let requiredText = ["Welcome back", "Email address", "Password", "Sign in"]
let missingText = requiredText.filter {
    !recognizedText.localizedCaseInsensitiveContains($0)
}

print(recognizedText)
if !missingText.isEmpty {
    fputs("iOS login screen is missing expected text: \(missingText.joined(separator: ", "))\n", stderr)
    exit(1)
}
