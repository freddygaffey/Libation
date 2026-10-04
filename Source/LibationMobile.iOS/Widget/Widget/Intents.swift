import AppIntents
import Foundation

/// Sets the speed, keeping the countdown right, and tells the app. One keychain read and two writes: iOS will not take
/// another tap on the widget until this returns and the widget is redrawn.
private func setSpeed(_ speed: Double, in loaded: WidgetState? = nil) {
    guard var state = loaded ?? WidgetState.load() else { return }
    let now = Date()
    state.settle(at: now)
    state.speed = min(WidgetState.maxSpeed, max(WidgetState.minSpeed, (speed * 100).rounded() / 100))
    state.save()
    WidgetCommand.post(WidgetCommand(command: "speed", bookId: state.bookId, speed: state.speed, at: now.timeIntervalSince1970))
}

/// The + and − speed buttons. Coarse steps go to the next half (2.7x up to 3x, down to 2.5x); fine ones by 0.1.
struct ChangeSpeedIntent: AppIntent {
    static var title: LocalizedStringResource = "Change speed"
    static var isDiscoverable = false

    @Parameter(title: "Faster") var faster: Bool
    @Parameter(title: "Fine") var fine: Bool

    init() {}
    init(faster: Bool, fine: Bool = false) {
        self.faster = faster
        self.fine = fine
    }

    func perform() async throws -> some IntentResult {
        guard let state = WidgetState.load() else { return .result() }
        let step = fine ? WidgetState.fineSpeedStep : WidgetState.speedStep
        let steps = state.speed / step
        setSpeed((faster ? (steps + 0.001).rounded(.down) + 1 : (steps - 0.001).rounded(.up) - 1) * step, in: state)
        return .result()
    }
}

/// A notch on a speed slider.
struct SetSpeedIntent: AppIntent {
    static var title: LocalizedStringResource = "Set speed"
    static var isDiscoverable = false

    @Parameter(title: "Speed") var speed: Double

    init() {}
    init(speed: Double) { self.speed = speed }

    func perform() async throws -> some IntentResult {
        setSpeed(speed)
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

/// The Control Centre play tile. Playing opens the app, which iOS may have closed while it was paused, with the
/// widget's play link. (openAppWhenRun would need the intent declared in the app as well, which the .NET app
/// cannot do; home-screen widgets use the link directly.)
@available(iOS 18.0, *)
struct PlayIntent: AppIntent {
    static var title: LocalizedStringResource = "Play"
    static var isDiscoverable = false

    func perform() async throws -> some IntentResult & OpensIntent {
        return .result(opensIntent: OpenURLIntent(WidgetLink.play))
    }
}

/// Skips by the app's skip length. Works while playing, when the app is running to hear it.
struct SkipIntent: AppIntent {
    static var title: LocalizedStringResource = "Skip"
    static var isDiscoverable = false

    @Parameter(title: "Forward") var forward: Bool

    init() {}
    init(forward: Bool) { self.forward = forward }

    func perform() async throws -> some IntentResult {
        guard var state = WidgetState.load() else { return .result() }
        let now = Date()
        state.settle(at: now)
        // Shown at once; the app's next update corrects it.
        let skip = (state.skipSeconds ?? 30) * (forward ? -1 : 1)
        state.remainingSeconds = max(0, state.remainingSeconds + skip)
        state.chapterRemainingSeconds = state.chapterRemainingSeconds.map { max(0, $0 + skip) }
        state.save()
        WidgetCommand.post(WidgetCommand(command: "skip", bookId: state.bookId, forward: forward, at: now.timeIntervalSince1970))
        return .result()
    }
}
