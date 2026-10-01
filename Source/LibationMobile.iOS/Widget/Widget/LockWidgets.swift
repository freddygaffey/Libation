import SwiftUI
import WidgetKit

// Lock-screen widgets. iOS draws these in its own tint, so they use the primary and secondary styles, not the
// app's colours.

/// Tapping one opens the app, where buttons are not available.
private struct LockFrame<Content: View>: View {
    let entry: Entry
    @ViewBuilder let content: (WidgetState) -> Content

    var body: some View {
        Group {
            if let state = entry.state {
                content(state)
            } else {
                Image(systemName: "headphones")
            }
        }
        .containerBackground(for: .widget) { Color.clear }
    }
}

// MARK: Speed Strip (rectangular): a tap-to-set slider.

struct SpeedStripView: View {
    let entry: Entry

    var body: some View {
        LockFrame(entry: entry) { state in
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 4) {
                    Image(systemName: "gauge.with.dots.needle.67percent")
                    Text(WidgetState.formatSpeed(state.speed)).fontWeight(.bold).widgetAccentable()
                    Text(state.title).foregroundStyle(.secondary).lineLimit(1)
                }
                .font(.system(size: 13))
                HStack(spacing: 3) {
                    SpeedStepButton(faster: false, width: 20, height: 18, tinted: true)
                    HorizontalSpeedSlider(speed: state.speed, stops: WidgetState.speedStops, barHeight: 7, labelSize: 9, tinted: true)
                    SpeedStepButton(faster: true, width: 20, height: 18, tinted: true)
                }
            }
        }
    }
}

struct SpeedStripWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationSpeedStrip", provider: Provider()) { SpeedStripView(entry: $0) }
            .configurationDisplayName("Speed Strip")
            .description("A speed slider for the lock screen, with − and + for 0.1 steps.")
            .supportedFamilies([.accessoryRectangular])
    }
}

// MARK: Now Playing strip (rectangular): title, chapter, progress.

struct NowPlayingStripView: View {
    let entry: Entry

    var body: some View {
        LockFrame(entry: entry) { state in
            VStack(alignment: .leading, spacing: 2) {
                HStack(spacing: 4) {
                    Image(systemName: state.isPlaying ? "play.fill" : "pause.fill").font(.system(size: 10))
                    Text(state.title).font(.system(size: 14, weight: .bold)).lineLimit(1).widgetAccentable()
                }
                Text(detail(state)).font(.system(size: 12)).foregroundStyle(.secondary).lineLimit(1)
                ProgressView(value: state.chapterProgress(at: entry.date) ?? state.bookProgress(at: entry.date))
                    .progressViewStyle(.linear)
            }
        }
    }

    private func detail(_ state: WidgetState) -> String {
        [state.chapterTitle, WidgetState.formatLeft(state.remaining(at: entry.date)), WidgetState.formatSpeed(state.speed)]
            .compactMap { $0 }.joined(separator: " · ")
    }
}

struct NowPlayingStripWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationNowPlayingStrip", provider: Provider()) { NowPlayingStripView(entry: $0) }
            .configurationDisplayName("Now Playing")
            .description("The book, chapter and time left.")
            .supportedFamilies([.accessoryRectangular])
    }
}

// MARK: Ring (circular): progress through the book, with the speed.

struct RingView: View {
    let entry: Entry

    var body: some View {
        LockFrame(entry: entry) { state in
            Gauge(value: state.bookProgress(at: entry.date)) {
                Image(systemName: "headphones")
            } currentValueLabel: {
                Text(WidgetState.formatSpeed(state.speed)).font(.system(size: 12, weight: .bold))
            }
            .gaugeStyle(.accessoryCircularCapacity)
            .widgetAccentable()
        }
    }
}

struct RingWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationRing", provider: Provider()) { RingView(entry: $0) }
            .configurationDisplayName("Progress Ring")
            .description("How far through the book, and the speed.")
            .supportedFamilies([.accessoryCircular])
    }
}

// MARK: Inline: one line above the clock.

struct InlineView: View {
    let entry: Entry

    var body: some View {
        LockFrame(entry: entry) { state in
            Text("\(Image(systemName: "headphones")) \(state.title) · \(WidgetState.formatLeft(state.remaining(at: entry.date)))")
        }
    }
}

struct InlineWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationInline", provider: Provider()) { InlineView(entry: $0) }
            .configurationDisplayName("Time Left")
            .description("The book and time left, above the clock.")
            .supportedFamilies([.accessoryInline])
    }
}
