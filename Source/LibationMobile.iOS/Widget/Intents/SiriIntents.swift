import AppIntents
import Foundation
import WidgetKit

// Siri and Shortcuts: "Hey Siri, set Libation to 7.3", "Speed up Libation by a half", "Pause Libation", "Play Libation".
//
// These live in an App Intents extension, which iOS 17 lets carry App Shortcuts, because the app itself is .NET and
// cannot declare intents. Like the widget's buttons, they change what the widget shows and tell the app through the
// shared keychain and a Darwin notification (WidgetState.swift); the app applies it, at once if it is running.

/// Speeds Siri understands by name: every tenth from 0.5x to 10x, said as "7.3", "seven point three", or for halves
/// "seven and a half".
enum SpeedChoice: String, AppEnum {
    case s0_5
    case s0_6
    case s0_7
    case s0_8
    case s0_9
    case s1_0
    case s1_1
    case s1_2
    case s1_3
    case s1_4
    case s1_5
    case s1_6
    case s1_7
    case s1_8
    case s1_9
    case s2_0
    case s2_1
    case s2_2
    case s2_3
    case s2_4
    case s2_5
    case s2_6
    case s2_7
    case s2_8
    case s2_9
    case s3_0
    case s3_1
    case s3_2
    case s3_3
    case s3_4
    case s3_5
    case s3_6
    case s3_7
    case s3_8
    case s3_9
    case s4_0
    case s4_1
    case s4_2
    case s4_3
    case s4_4
    case s4_5
    case s4_6
    case s4_7
    case s4_8
    case s4_9
    case s5_0
    case s5_1
    case s5_2
    case s5_3
    case s5_4
    case s5_5
    case s5_6
    case s5_7
    case s5_8
    case s5_9
    case s6_0
    case s6_1
    case s6_2
    case s6_3
    case s6_4
    case s6_5
    case s6_6
    case s6_7
    case s6_8
    case s6_9
    case s7_0
    case s7_1
    case s7_2
    case s7_3
    case s7_4
    case s7_5
    case s7_6
    case s7_7
    case s7_8
    case s7_9
    case s8_0
    case s8_1
    case s8_2
    case s8_3
    case s8_4
    case s8_5
    case s8_6
    case s8_7
    case s8_8
    case s8_9
    case s9_0
    case s9_1
    case s9_2
    case s9_3
    case s9_4
    case s9_5
    case s9_6
    case s9_7
    case s9_8
    case s9_9
    case s10_0

    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Speed"
    static var caseDisplayRepresentations: [SpeedChoice: DisplayRepresentation] = [
        .s0_5: DisplayRepresentation(title: "0.5×", synonyms: ["0.5", "zero point five", ".5", "point five", "half", "a half", "half speed"]),
        .s0_6: DisplayRepresentation(title: "0.6×", synonyms: ["0.6", "zero point six", ".6", "point six"]),
        .s0_7: DisplayRepresentation(title: "0.7×", synonyms: ["0.7", "zero point seven", ".7", "point seven"]),
        .s0_8: DisplayRepresentation(title: "0.8×", synonyms: ["0.8", "zero point eight", ".8", "point eight"]),
        .s0_9: DisplayRepresentation(title: "0.9×", synonyms: ["0.9", "zero point nine", ".9", "point nine"]),
        .s1_0: DisplayRepresentation(title: "1×", synonyms: ["1", "1.0", "one", "one times", "one x", "one point zero"]),
        .s1_1: DisplayRepresentation(title: "1.1×", synonyms: ["1.1", "one point one"]),
        .s1_2: DisplayRepresentation(title: "1.2×", synonyms: ["1.2", "one point two"]),
        .s1_3: DisplayRepresentation(title: "1.3×", synonyms: ["1.3", "one point three"]),
        .s1_4: DisplayRepresentation(title: "1.4×", synonyms: ["1.4", "one point four"]),
        .s1_5: DisplayRepresentation(title: "1.5×", synonyms: ["1.5", "one point five", "one and a half"]),
        .s1_6: DisplayRepresentation(title: "1.6×", synonyms: ["1.6", "one point six"]),
        .s1_7: DisplayRepresentation(title: "1.7×", synonyms: ["1.7", "one point seven"]),
        .s1_8: DisplayRepresentation(title: "1.8×", synonyms: ["1.8", "one point eight"]),
        .s1_9: DisplayRepresentation(title: "1.9×", synonyms: ["1.9", "one point nine"]),
        .s2_0: DisplayRepresentation(title: "2×", synonyms: ["2", "2.0", "two", "two times", "two x", "two point zero"]),
        .s2_1: DisplayRepresentation(title: "2.1×", synonyms: ["2.1", "two point one"]),
        .s2_2: DisplayRepresentation(title: "2.2×", synonyms: ["2.2", "two point two"]),
        .s2_3: DisplayRepresentation(title: "2.3×", synonyms: ["2.3", "two point three"]),
        .s2_4: DisplayRepresentation(title: "2.4×", synonyms: ["2.4", "two point four"]),
        .s2_5: DisplayRepresentation(title: "2.5×", synonyms: ["2.5", "two point five", "two and a half"]),
        .s2_6: DisplayRepresentation(title: "2.6×", synonyms: ["2.6", "two point six"]),
        .s2_7: DisplayRepresentation(title: "2.7×", synonyms: ["2.7", "two point seven"]),
        .s2_8: DisplayRepresentation(title: "2.8×", synonyms: ["2.8", "two point eight"]),
        .s2_9: DisplayRepresentation(title: "2.9×", synonyms: ["2.9", "two point nine"]),
        .s3_0: DisplayRepresentation(title: "3×", synonyms: ["3", "3.0", "three", "three times", "three x", "three point zero"]),
        .s3_1: DisplayRepresentation(title: "3.1×", synonyms: ["3.1", "three point one"]),
        .s3_2: DisplayRepresentation(title: "3.2×", synonyms: ["3.2", "three point two"]),
        .s3_3: DisplayRepresentation(title: "3.3×", synonyms: ["3.3", "three point three"]),
        .s3_4: DisplayRepresentation(title: "3.4×", synonyms: ["3.4", "three point four"]),
        .s3_5: DisplayRepresentation(title: "3.5×", synonyms: ["3.5", "three point five", "three and a half"]),
        .s3_6: DisplayRepresentation(title: "3.6×", synonyms: ["3.6", "three point six"]),
        .s3_7: DisplayRepresentation(title: "3.7×", synonyms: ["3.7", "three point seven"]),
        .s3_8: DisplayRepresentation(title: "3.8×", synonyms: ["3.8", "three point eight"]),
        .s3_9: DisplayRepresentation(title: "3.9×", synonyms: ["3.9", "three point nine"]),
        .s4_0: DisplayRepresentation(title: "4×", synonyms: ["4", "4.0", "four", "four times", "four x", "four point zero"]),
        .s4_1: DisplayRepresentation(title: "4.1×", synonyms: ["4.1", "four point one"]),
        .s4_2: DisplayRepresentation(title: "4.2×", synonyms: ["4.2", "four point two"]),
        .s4_3: DisplayRepresentation(title: "4.3×", synonyms: ["4.3", "four point three"]),
        .s4_4: DisplayRepresentation(title: "4.4×", synonyms: ["4.4", "four point four"]),
        .s4_5: DisplayRepresentation(title: "4.5×", synonyms: ["4.5", "four point five", "four and a half"]),
        .s4_6: DisplayRepresentation(title: "4.6×", synonyms: ["4.6", "four point six"]),
        .s4_7: DisplayRepresentation(title: "4.7×", synonyms: ["4.7", "four point seven"]),
        .s4_8: DisplayRepresentation(title: "4.8×", synonyms: ["4.8", "four point eight"]),
        .s4_9: DisplayRepresentation(title: "4.9×", synonyms: ["4.9", "four point nine"]),
        .s5_0: DisplayRepresentation(title: "5×", synonyms: ["5", "5.0", "five", "five times", "five x", "five point zero"]),
        .s5_1: DisplayRepresentation(title: "5.1×", synonyms: ["5.1", "five point one"]),
        .s5_2: DisplayRepresentation(title: "5.2×", synonyms: ["5.2", "five point two"]),
        .s5_3: DisplayRepresentation(title: "5.3×", synonyms: ["5.3", "five point three"]),
        .s5_4: DisplayRepresentation(title: "5.4×", synonyms: ["5.4", "five point four"]),
        .s5_5: DisplayRepresentation(title: "5.5×", synonyms: ["5.5", "five point five", "five and a half"]),
        .s5_6: DisplayRepresentation(title: "5.6×", synonyms: ["5.6", "five point six"]),
        .s5_7: DisplayRepresentation(title: "5.7×", synonyms: ["5.7", "five point seven"]),
        .s5_8: DisplayRepresentation(title: "5.8×", synonyms: ["5.8", "five point eight"]),
        .s5_9: DisplayRepresentation(title: "5.9×", synonyms: ["5.9", "five point nine"]),
        .s6_0: DisplayRepresentation(title: "6×", synonyms: ["6", "6.0", "six", "six times", "six x", "six point zero"]),
        .s6_1: DisplayRepresentation(title: "6.1×", synonyms: ["6.1", "six point one"]),
        .s6_2: DisplayRepresentation(title: "6.2×", synonyms: ["6.2", "six point two"]),
        .s6_3: DisplayRepresentation(title: "6.3×", synonyms: ["6.3", "six point three"]),
        .s6_4: DisplayRepresentation(title: "6.4×", synonyms: ["6.4", "six point four"]),
        .s6_5: DisplayRepresentation(title: "6.5×", synonyms: ["6.5", "six point five", "six and a half"]),
        .s6_6: DisplayRepresentation(title: "6.6×", synonyms: ["6.6", "six point six"]),
        .s6_7: DisplayRepresentation(title: "6.7×", synonyms: ["6.7", "six point seven"]),
        .s6_8: DisplayRepresentation(title: "6.8×", synonyms: ["6.8", "six point eight"]),
        .s6_9: DisplayRepresentation(title: "6.9×", synonyms: ["6.9", "six point nine"]),
        .s7_0: DisplayRepresentation(title: "7×", synonyms: ["7", "7.0", "seven", "seven times", "seven x", "seven point zero"]),
        .s7_1: DisplayRepresentation(title: "7.1×", synonyms: ["7.1", "seven point one"]),
        .s7_2: DisplayRepresentation(title: "7.2×", synonyms: ["7.2", "seven point two"]),
        .s7_3: DisplayRepresentation(title: "7.3×", synonyms: ["7.3", "seven point three"]),
        .s7_4: DisplayRepresentation(title: "7.4×", synonyms: ["7.4", "seven point four"]),
        .s7_5: DisplayRepresentation(title: "7.5×", synonyms: ["7.5", "seven point five", "seven and a half"]),
        .s7_6: DisplayRepresentation(title: "7.6×", synonyms: ["7.6", "seven point six"]),
        .s7_7: DisplayRepresentation(title: "7.7×", synonyms: ["7.7", "seven point seven"]),
        .s7_8: DisplayRepresentation(title: "7.8×", synonyms: ["7.8", "seven point eight"]),
        .s7_9: DisplayRepresentation(title: "7.9×", synonyms: ["7.9", "seven point nine"]),
        .s8_0: DisplayRepresentation(title: "8×", synonyms: ["8", "8.0", "eight", "eight times", "eight x", "eight point zero"]),
        .s8_1: DisplayRepresentation(title: "8.1×", synonyms: ["8.1", "eight point one"]),
        .s8_2: DisplayRepresentation(title: "8.2×", synonyms: ["8.2", "eight point two"]),
        .s8_3: DisplayRepresentation(title: "8.3×", synonyms: ["8.3", "eight point three"]),
        .s8_4: DisplayRepresentation(title: "8.4×", synonyms: ["8.4", "eight point four"]),
        .s8_5: DisplayRepresentation(title: "8.5×", synonyms: ["8.5", "eight point five", "eight and a half"]),
        .s8_6: DisplayRepresentation(title: "8.6×", synonyms: ["8.6", "eight point six"]),
        .s8_7: DisplayRepresentation(title: "8.7×", synonyms: ["8.7", "eight point seven"]),
        .s8_8: DisplayRepresentation(title: "8.8×", synonyms: ["8.8", "eight point eight"]),
        .s8_9: DisplayRepresentation(title: "8.9×", synonyms: ["8.9", "eight point nine"]),
        .s9_0: DisplayRepresentation(title: "9×", synonyms: ["9", "9.0", "nine", "nine times", "nine x", "nine point zero"]),
        .s9_1: DisplayRepresentation(title: "9.1×", synonyms: ["9.1", "nine point one"]),
        .s9_2: DisplayRepresentation(title: "9.2×", synonyms: ["9.2", "nine point two"]),
        .s9_3: DisplayRepresentation(title: "9.3×", synonyms: ["9.3", "nine point three"]),
        .s9_4: DisplayRepresentation(title: "9.4×", synonyms: ["9.4", "nine point four"]),
        .s9_5: DisplayRepresentation(title: "9.5×", synonyms: ["9.5", "nine point five", "nine and a half"]),
        .s9_6: DisplayRepresentation(title: "9.6×", synonyms: ["9.6", "nine point six"]),
        .s9_7: DisplayRepresentation(title: "9.7×", synonyms: ["9.7", "nine point seven"]),
        .s9_8: DisplayRepresentation(title: "9.8×", synonyms: ["9.8", "nine point eight"]),
        .s9_9: DisplayRepresentation(title: "9.9×", synonyms: ["9.9", "nine point nine"]),
        .s10_0: DisplayRepresentation(title: "10×", synonyms: ["10", "10.0", "ten", "ten times", "ten x", "ten point zero"]),
    ]

    /// "s7_3" is 7.3.
    var value: Double {
        let parts = rawValue.dropFirst().split(separator: "_")
        return Double(parts[0])! + Double(parts[1])! / 10
    }
}

/// Amounts to speed up or slow down by: every tenth from 0.1 to 2, said as "0.1", "point one", "a tenth", "a half".
enum SpeedChange: String, AppEnum {
    case d0_1
    case d0_2
    case d0_3
    case d0_4
    case d0_5
    case d0_6
    case d0_7
    case d0_8
    case d0_9
    case d1_0
    case d1_1
    case d1_2
    case d1_3
    case d1_4
    case d1_5
    case d1_6
    case d1_7
    case d1_8
    case d1_9
    case d2_0

    static var typeDisplayRepresentation: TypeDisplayRepresentation = "Amount"
    static var caseDisplayRepresentations: [SpeedChange: DisplayRepresentation] = [
        .d0_1: DisplayRepresentation(title: "0.1×", synonyms: ["0.1", "zero point one", ".1", "point one", "a tenth", "one tenth"]),
        .d0_2: DisplayRepresentation(title: "0.2×", synonyms: ["0.2", "zero point two", ".2", "point two", "a fifth", "one fifth", "two tenths"]),
        .d0_3: DisplayRepresentation(title: "0.3×", synonyms: ["0.3", "zero point three", ".3", "point three"]),
        .d0_4: DisplayRepresentation(title: "0.4×", synonyms: ["0.4", "zero point four", ".4", "point four"]),
        .d0_5: DisplayRepresentation(title: "0.5×", synonyms: ["0.5", "zero point five", ".5", "point five", "a half", "half", "one half"]),
        .d0_6: DisplayRepresentation(title: "0.6×", synonyms: ["0.6", "zero point six", ".6", "point six"]),
        .d0_7: DisplayRepresentation(title: "0.7×", synonyms: ["0.7", "zero point seven", ".7", "point seven"]),
        .d0_8: DisplayRepresentation(title: "0.8×", synonyms: ["0.8", "zero point eight", ".8", "point eight"]),
        .d0_9: DisplayRepresentation(title: "0.9×", synonyms: ["0.9", "zero point nine", ".9", "point nine"]),
        .d1_0: DisplayRepresentation(title: "1×", synonyms: ["1.0", "one point zero", "1", "one"]),
        .d1_1: DisplayRepresentation(title: "1.1×", synonyms: ["1.1", "one point one"]),
        .d1_2: DisplayRepresentation(title: "1.2×", synonyms: ["1.2", "one point two"]),
        .d1_3: DisplayRepresentation(title: "1.3×", synonyms: ["1.3", "one point three"]),
        .d1_4: DisplayRepresentation(title: "1.4×", synonyms: ["1.4", "one point four"]),
        .d1_5: DisplayRepresentation(title: "1.5×", synonyms: ["1.5", "one point five", "one and a half"]),
        .d1_6: DisplayRepresentation(title: "1.6×", synonyms: ["1.6", "one point six"]),
        .d1_7: DisplayRepresentation(title: "1.7×", synonyms: ["1.7", "one point seven"]),
        .d1_8: DisplayRepresentation(title: "1.8×", synonyms: ["1.8", "one point eight"]),
        .d1_9: DisplayRepresentation(title: "1.9×", synonyms: ["1.9", "one point nine"]),
        .d2_0: DisplayRepresentation(title: "2×", synonyms: ["2.0", "two point zero", "2", "two"]),
    ]

    /// "s7_3" is 7.3.
    var value: Double {
        let parts = rawValue.dropFirst().split(separator: "_")
        return Double(parts[0])! + Double(parts[1])! / 10
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

struct SpeedUpByIntent: AppIntent {
    static var title: LocalizedStringResource = "Speed up by"
    static var description = IntentDescription("Play the book faster by an amount, such as 0.1x or a half.")

    @Parameter(title: "Amount") var amount: SpeedChange

    func perform() async throws -> some IntentResult & ProvidesDialog {
        .result(dialog: "\(applySpeed { $0 + amount.value })")
    }
}

struct SlowDownByIntent: AppIntent {
    static var title: LocalizedStringResource = "Slow down by"
    static var description = IntentDescription("Play the book slower by an amount, such as 0.1x or a half.")

    @Parameter(title: "Amount") var amount: SpeedChange

    func perform() async throws -> some IntentResult & ProvidesDialog {
        .result(dialog: "\(applySpeed { $0 - amount.value })")
    }
}

struct SetSpeedToIntent: AppIntent {
    static var title: LocalizedStringResource = "Set speed"
    static var description = IntentDescription("Play the book at a speed from 0.5x to 10x, to a tenth.")

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
struct PlayBookIntent: AppIntent {
    static var title: LocalizedStringResource = "Play"
    static var description = IntentDescription("Play the last book.")

    func perform() async throws -> some IntentResult & OpensIntent {
        .result(opensIntent: OpenURLIntent(WidgetLink.play))
    }
}

/// "Hey Siri, tell Libation", then anything: "seven point three", "a bit faster", "slower by a tenth", "pause",
/// "what's my speed". Siri only matches set phrases, so this takes whatever it heard and reads it itself
/// (SpokenCommand.swift), asking again if it cannot.
struct SpokenCommandIntent: AppIntent {
    static var title: LocalizedStringResource = "Tell Libation"
    static var description = IntentDescription("Change speed, pause or ask the speed, in your own words.")

    @Parameter(title: "What to do", requestValueDialog: "What should Libation do?") var request: String

    func perform() async throws -> some IntentResult & ProvidesDialog {
        guard let command = SpokenCommand.parse(request) else {
            throw $request.needsValueError("Say a speed like 7.3, or faster, slower by a tenth, or pause.")
        }
        switch command {
        case .setSpeed(let speed):
            return .result(dialog: "\(applySpeed { _ in speed })")
        case .change(let amount):
            return .result(dialog: "\(applySpeed { $0 + amount })")
        case .current:
            guard let state = WidgetState.load() else { return .result(dialog: "Nothing is playing in Libation.") }
            return .result(dialog: "\(state.title) is at \(WidgetState.formatSpeed(state.speed)), \(WidgetState.formatLeft(state.remaining(at: Date()))).")
        case .pause:
            _ = try await PauseBookIntent().perform()
            return .result(dialog: "Paused.")
        case .play:
            // Only reaches a running app; "Play Libation" opens it if iOS has closed it.
            WidgetCommand.post(WidgetCommand(command: "play", bookId: WidgetState.load()?.bookId, at: Date().timeIntervalSince1970))
            return .result(dialog: "Playing.")
        case .skip(let forward):
            WidgetCommand.post(WidgetCommand(command: "skip", bookId: WidgetState.load()?.bookId, forward: forward, at: Date().timeIntervalSince1970))
            return .result(dialog: forward ? "Skipped forward." : "Skipped back.")
        }
    }
}

struct LibationShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(intent: SpokenCommandIntent(), phrases: [
            "Tell \(.applicationName)",
            "Ask \(.applicationName)",
            "\(.applicationName) command",
            "Change \(.applicationName)",
            "Talk to \(.applicationName)",
        ], shortTitle: "Tell Libation", systemImageName: "waveform")
        AppShortcut(intent: SetSpeedToIntent(), phrases: [
            "Set \(.applicationName) to \(\.$speed)",
            "Set \(.applicationName) speed to \(\.$speed)",
            "Play \(.applicationName) at \(\.$speed)",
            "\(.applicationName) \(\.$speed)",
            "Set speed in \(.applicationName)",
        ], shortTitle: "Set speed", systemImageName: "gauge.with.dots.needle.67percent")
        AppShortcut(intent: SpeedUpByIntent(), phrases: [
            "Speed up \(.applicationName) by \(\.$amount)",
            "Increase \(.applicationName) speed by \(\.$amount)",
            "Make \(.applicationName) faster by \(\.$amount)",
        ], shortTitle: "Speed up by", systemImageName: "hare.fill")
        AppShortcut(intent: SlowDownByIntent(), phrases: [
            "Slow down \(.applicationName) by \(\.$amount)",
            "Decrease \(.applicationName) speed by \(\.$amount)",
            "Make \(.applicationName) slower by \(\.$amount)",
        ], shortTitle: "Slow down by", systemImageName: "tortoise.fill")
        AppShortcut(intent: SpeedUpIntent(), phrases: [
            "Speed up \(.applicationName)",
            "Increase \(.applicationName) speed",
            "Faster in \(.applicationName)",
            "Make \(.applicationName) faster",
        ], shortTitle: "Speed up", systemImageName: "hare")
        AppShortcut(intent: SlowDownIntent(), phrases: [
            "Slow down \(.applicationName)",
            "Decrease \(.applicationName) speed",
            "Slower in \(.applicationName)",
            "Make \(.applicationName) slower",
        ], shortTitle: "Slow down", systemImageName: "tortoise")
        AppShortcut(intent: CurrentSpeedIntent(), phrases: [
            "What speed is \(.applicationName) at",
            "\(.applicationName) speed",
            "What's my speed in \(.applicationName)",
            "How long is left in \(.applicationName)",
        ], shortTitle: "Current speed", systemImageName: "speaker.wave.2")
        AppShortcut(intent: PauseBookIntent(), phrases: [
            "Pause \(.applicationName)",
            "Stop \(.applicationName)",
        ], shortTitle: "Pause", systemImageName: "pause.fill")
        AppShortcut(intent: PlayBookIntent(), phrases: [
            "Play \(.applicationName)",
            "Resume \(.applicationName)",
            "Start \(.applicationName)",
        ], shortTitle: "Play", systemImageName: "play.fill")
    }
}
