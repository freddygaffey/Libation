import AppIntents
import Foundation
import WidgetKit

// Siri and Shortcuts: "Hey Siri, speed up Libation", "Set Libation to seven", "Pause Libation".
//
// These live in an App Intents extension, which iOS 17 lets carry App Shortcuts, because the app itself is .NET and
// cannot declare intents. Like the widget's buttons, they change what the widget shows and tell the app through the
// shared keychain and a Darwin notification (WidgetState.swift); the app applies it, at once if it is running.

/// Speeds Siri understands by name, every half from 1x to 10x.
enum SpeedChoice: String, AppEnum {
    case x1
    case x1_5
    case x2
    case x2_5
    case x3
    case x3_5
    case x4
    case x4_5
    case x5
    case x5_5
    case x6
    case x6_5
    case x7
    case x7_5
    case x8
    case x8_5
    case x9
    case x9_5
    case x10

    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Speed"
    static var caseDisplayRepresentations: [SpeedChoice: DisplayRepresentation] = [
        .x1: DisplayRepresentation(title: "1×", synonyms: ["1", "one", "one times", "one x"]),
        .x1_5: DisplayRepresentation(title: "1.5×", synonyms: ["1.5", "one point five", "one and a half"]),
        .x2: DisplayRepresentation(title: "2×", synonyms: ["2", "two", "two times", "two x"]),
        .x2_5: DisplayRepresentation(title: "2.5×", synonyms: ["2.5", "two point five", "two and a half"]),
        .x3: DisplayRepresentation(title: "3×", synonyms: ["3", "three", "three times", "three x"]),
        .x3_5: DisplayRepresentation(title: "3.5×", synonyms: ["3.5", "three point five", "three and a half"]),
        .x4: DisplayRepresentation(title: "4×", synonyms: ["4", "four", "four times", "four x"]),
        .x4_5: DisplayRepresentation(title: "4.5×", synonyms: ["4.5", "four point five", "four and a half"]),
        .x5: DisplayRepresentation(title: "5×", synonyms: ["5", "five", "five times", "five x"]),
        .x5_5: DisplayRepresentation(title: "5.5×", synonyms: ["5.5", "five point five", "five and a half"]),
        .x6: DisplayRepresentation(title: "6×", synonyms: ["6", "six", "six times", "six x"]),
        .x6_5: DisplayRepresentation(title: "6.5×", synonyms: ["6.5", "six point five", "six and a half"]),
        .x7: DisplayRepresentation(title: "7×", synonyms: ["7", "seven", "seven times", "seven x"]),
        .x7_5: DisplayRepresentation(title: "7.5×", synonyms: ["7.5", "seven point five", "seven and a half"]),
        .x8: DisplayRepresentation(title: "8×", synonyms: ["8", "eight", "eight times", "eight x"]),
        .x8_5: DisplayRepresentation(title: "8.5×", synonyms: ["8.5", "eight point five", "eight and a half"]),
        .x9: DisplayRepresentation(title: "9×", synonyms: ["9", "nine", "nine times", "nine x"]),
        .x9_5: DisplayRepresentation(title: "9.5×", synonyms: ["9.5", "nine point five", "nine and a half"]),
        .x10: DisplayRepresentation(title: "10×", synonyms: ["10", "ten", "ten times", "ten x"]),
    ]

    var value: Double {
        let parts = rawValue.dropFirst().split(separator: "_")
        return Double(parts[0])! + (parts.count > 1 ? 0.5 : 0)
    }
}

/// Applies a speed for the book in the widget's state, and says what it is now.
private func applySpeed(_ change: (Double) -> Double) -> String {
    guard var state = WidgetState.load() else { return "Play a book in Libation first." }
    let now = Date()
    state.settle(at: now)
    state.speed = min(WidgetState.maxSpeed, max(WidgetState.minSpeed, (change(state.speed) * 10).rounded() / 10))
    state.save()
    WidgetCommand.post(WidgetCommand(command: "speed", bookId: state.bookId, speed: state.speed, at: now.timeIntervalSince1970))
    WidgetCenter.shared.reloadAllTimelines()
    return "Libation is at \(WidgetState.formatSpeed(state.speed))."
}

struct SpeedUpIntent: AppIntent {
    static var title: LocalizedStringResource = "Speed up"
    static var description = IntentDescription("Play the book half a speed faster.")

    func perform() async throws -> some IntentResult & ProvidesDialog {
        .result(dialog: "\(applySpeed { $0 + 0.5 })")
    }
}

struct SlowDownIntent: AppIntent {
    static var title: LocalizedStringResource = "Slow down"
    static var description = IntentDescription("Play the book half a speed slower.")

    func perform() async throws -> some IntentResult & ProvidesDialog {
        .result(dialog: "\(applySpeed { $0 - 0.5 })")
    }
}

struct SetSpeedToIntent: AppIntent {
    static var title: LocalizedStringResource = "Set speed"
    static var description = IntentDescription("Play the book at a speed from 1x to 10x.")

    @Parameter(title: "Speed") var speed: SpeedChoice

    func perform() async throws -> some IntentResult & ProvidesDialog {
        .result(dialog: "\(applySpeed { _ in speed.value })")
    }
}

/// Any speed, such as 6.3, for Shortcuts; Siri asks for the number.
struct SetExactSpeedIntent: AppIntent {
    static var title: LocalizedStringResource = "Set exact speed"
    static var description = IntentDescription("Play the book at any speed from 0.5x to 10x, to a tenth.")

    @Parameter(title: "Speed", inclusiveRange: (0.5, 10)) var speed: Double

    func perform() async throws -> some IntentResult & ProvidesDialog {
        .result(dialog: "\(applySpeed { _ in speed })")
    }
}

struct CurrentSpeedIntent: AppIntent {
    static var title: LocalizedStringResource = "Current speed"
    static var description = IntentDescription("Say the speed and the time left.")

    func perform() async throws -> some IntentResult & ProvidesDialog {
        guard let state = WidgetState.load() else { return .result(dialog: "Nothing is playing in Libation.") }
        return .result(dialog: "\(state.title) is at \(WidgetState.formatSpeed(state.speed)), \(WidgetState.formatLeft(state.remaining(at: Date()))).")
    }
}

struct PauseBookIntent: AppIntent {
    static var title: LocalizedStringResource = "Pause"
    static var description = IntentDescription("Pause the book.")

    func perform() async throws -> some IntentResult {
        guard var state = WidgetState.load() else { return .result() }
        let now = Date()
        state.settle(at: now)
        state.isPlaying = false
        state.save()
        WidgetCommand.post(WidgetCommand(command: "pause", bookId: state.bookId, at: now.timeIntervalSince1970))
        WidgetCenter.shared.reloadAllTimelines()
        return .result()
    }
}

/// Playing needs the app, which iOS may have closed: it opens with the widget's play link.
@available(iOS 18.0, *)
struct PlayBookIntent: AppIntent {
    static var title: LocalizedStringResource = "Play"
    static var description = IntentDescription("Play the last book.")

    func perform() async throws -> some IntentResult & OpensIntent {
        .result(opensIntent: OpenURLIntent(WidgetLink.play))
    }
}

struct LibationShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: SetSpeedToIntent(), phrases: [
            "Set \(.applicationName) to \(\.$speed)",
            "Set \(.applicationName) speed to \(\.$speed)",
            "Play \(.applicationName) at \(\.$speed)",
            "\(.applicationName) \(\.$speed)",
            "Set speed in \(.applicationName)",
        ], shortTitle: "Set speed", systemImageName: "gauge.with.dots.needle.67percent")
        AppShortcut(intent: SpeedUpIntent(), phrases: [
            "Speed up \(.applicationName)",
            "Faster in \(.applicationName)",
            "Make \(.applicationName) faster",
        ], shortTitle: "Speed up", systemImageName: "hare")
        AppShortcut(intent: SlowDownIntent(), phrases: [
            "Slow down \(.applicationName)",
            "Slower in \(.applicationName)",
            "Make \(.applicationName) slower",
        ], shortTitle: "Slow down", systemImageName: "tortoise")
        AppShortcut(intent: CurrentSpeedIntent(), phrases: [
            "What speed is \(.applicationName) at",
            "\(.applicationName) speed",
            "How long is left in \(.applicationName)",
        ], shortTitle: "Current speed", systemImageName: "speaker.wave.2")
        AppShortcut(intent: PauseBookIntent(), phrases: [
            "Pause \(.applicationName)",
            "Stop \(.applicationName)",
        ], shortTitle: "Pause", systemImageName: "pause.fill")
    }
}
