import AppIntents
import Foundation

/// The widget's speed buttons. Steps to the next half: 2.7x goes up to 3x and down to 2.5x.
struct ChangeSpeedIntent: AppIntent {
    static var title: LocalizedStringResource = "Change speed"
    static var isDiscoverable = false

    @Parameter(title: "Faster") var faster: Bool

    init() {}
    init(faster: Bool) { self.faster = faster }

    func perform() async throws -> some IntentResult {
        guard var state = WidgetState.load() else { return .result() }
        let step = WidgetState.speedStep
        let steps = state.speed / step
        let next = (faster ? (steps + 0.001).rounded(.down) + 1 : (steps - 0.001).rounded(.up) - 1) * step
        let now = Date()
        state.settle(at: now)
        state.speed = min(WidgetState.maxSpeed, max(WidgetState.minSpeed, next))
        state.save()
        WidgetCommand.post(WidgetCommand(command: "speed", bookId: state.bookId, speed: state.speed, at: now.timeIntervalSince1970))
        return .result()
    }
}

/// Pausing needs no app on screen: the app is running, because it is playing.
struct PauseIntent: AppIntent {
    static var title: LocalizedStringResource = "Pause"
    static var isDiscoverable = false

    func perform() async throws -> some IntentResult {
        guard var state = WidgetState.load() else { return .result() }
        let now = Date()
        state.settle(at: now)
        state.isPlaying = false
        state.save()
        WidgetCommand.post(WidgetCommand(command: "pause", bookId: state.bookId, at: now.timeIntervalSince1970))
        return .result()
    }
}

/// Playing opens the app, which iOS may have closed while it was paused, and it starts the last book.
struct PlayIntent: AppIntent {
    static var title: LocalizedStringResource = "Play"
    static var isDiscoverable = false
    static var openAppWhenRun = true

    func perform() async throws -> some IntentResult {
        WidgetCommand.post(WidgetCommand(command: "play", bookId: WidgetState.load()?.bookId, at: Date().timeIntervalSince1970))
        return .result()
    }
}
