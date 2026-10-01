import AppIntents
import SwiftUI
import WidgetKit

// Control Centre tiles (iOS 18): buttons only, no sliders.

// Two tiles rather than one toggle: playing has to open the app (iOS may have closed it), pausing does not,
// and a tile's action cannot change with the state.
@available(iOS 18.0, *)
struct PlayControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "LibationPlayControl") {
            ControlWidgetButton(action: PlayIntent()) {
                Label("Play", systemImage: "play.fill")
            }
        }
        .displayName("Play")
        .description("Plays your book.")
    }
}

@available(iOS 18.0, *)
struct PauseControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "LibationPauseControl") {
            ControlWidgetButton(action: PauseIntent()) {
                Label("Pause", systemImage: "pause.fill")
            }
        }
        .displayName("Pause")
        .description("Pauses your book.")
    }
}

@available(iOS 18.0, *)
struct FasterControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "LibationFasterControl") {
            ControlWidgetButton(action: ChangeSpeedIntent(faster: true, fine: true)) {
                Label(WidgetState.load().map { WidgetState.formatSpeed($0.speed) } ?? "Faster", systemImage: "hare.fill")
            }
        }
        .displayName("Faster")
        .description("Speeds the book up by 0.1x.")
    }
}

@available(iOS 18.0, *)
struct SlowerControl: ControlWidget {
    var body: some ControlWidgetConfiguration {
        StaticControlConfiguration(kind: "LibationSlowerControl") {
            ControlWidgetButton(action: ChangeSpeedIntent(faster: false, fine: true)) {
                Label(WidgetState.load().map { WidgetState.formatSpeed($0.speed) } ?? "Slower", systemImage: "tortoise.fill")
            }
        }
        .displayName("Slower")
        .description("Slows the book down by 0.1x.")
    }
}
