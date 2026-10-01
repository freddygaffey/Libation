import SwiftUI
import WidgetKit

// MARK: Now Playing (small): cover, title, play, time left, speed buttons. Like Audible's.

struct NowPlayingSmall: View {
    let entry: Entry

    var body: some View {
        WidgetFrame(entry: entry) { state in
            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .top) {
                    Cover(image: entry.cover, size: 58)
                    Spacer(minLength: 4)
                    VStack(spacing: 2) {
                        SpeedStepButton(faster: true)
                        SpeedLabel(speed: state.speed)
                        SpeedStepButton(faster: false)
                    }
                }
                Spacer(minLength: 4)
                Text(state.title)
                    .font(.system(size: 15, weight: .bold))
                    .foregroundStyle(Color.paper)
                    .lineLimit(1)
                Spacer(minLength: 4)
                HStack(spacing: 8) {
                    PlayButton(isPlaying: state.isPlaying, size: 36)
                    Text(WidgetState.formatLeft(state.remaining(at: entry.date)))
                        .font(.system(size: 13, weight: .medium))
                        .foregroundStyle(Color.dust)
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
            }
        }
    }
}

struct NowPlayingWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationNowPlaying", provider: Provider()) { NowPlayingSmall(entry: $0) }
            .configurationDisplayName("Now Playing")
            .description("The book you last played, with play and speed.")
            .supportedFamilies([.systemSmall])
    }
}

// MARK: Speed Dial (small): an upright slider beside the cover.

struct SpeedDialSmall: View {
    let entry: Entry

    var body: some View {
        WidgetFrame(entry: entry) { state in
            HStack(spacing: 8) {
                VStack(alignment: .leading, spacing: 4) {
                    Cover(image: entry.cover, size: 46)
                    Text(state.title)
                        .font(.system(size: 12, weight: .bold))
                        .foregroundStyle(Color.paper)
                        .lineLimit(1)
                    Spacer(minLength: 0)
                    SpeedLabel(speed: state.speed, size: 13)
                    PlayButton(isPlaying: state.isPlaying, size: 32)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                VerticalSpeedSlider(speed: state.speed, stops: WidgetState.speedStops)
            }
        }
    }
}

struct SpeedDialWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationSpeedDial", provider: Provider()) { SpeedDialSmall(entry: $0) }
            .configurationDisplayName("Speed Dial")
            .description("Tap a speed on the slider, or − and + for 0.1 steps.")
            .supportedFamilies([.systemSmall])
    }
}

// MARK: Speed Bar (small): a slider across, with fine + and −.

struct SpeedBarSmall: View {
    let entry: Entry

    var body: some View {
        WidgetFrame(entry: entry) { state in
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 8) {
                    Cover(image: entry.cover, size: 44)
                    VStack(alignment: .leading, spacing: 4) {
                        Text(state.title)
                            .font(.system(size: 12, weight: .bold))
                            .foregroundStyle(Color.paper)
                            .lineLimit(1)
                        PlayButton(isPlaying: state.isPlaying, size: 26)
                    }
                }
                Spacer(minLength: 4)
                HStack {
                    SpeedStepButton(faster: false, width: 30, height: 20)
                    Spacer(minLength: 2)
                    SpeedLabel(speed: state.speed, size: 16)
                    Spacer(minLength: 2)
                    SpeedStepButton(faster: true, width: 30, height: 20)
                }
                Spacer(minLength: 4)
                HorizontalSpeedSlider(speed: state.speed, stops: WidgetState.speedStops)
            }
        }
    }
}

struct SpeedBarWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationSpeedBar", provider: Provider()) { SpeedBarSmall(entry: $0) }
            .configurationDisplayName("Speed Bar")
            .description("A speed slider, with − and + for 0.1 steps.")
            .supportedFamilies([.systemSmall])
    }
}

// MARK: Player (medium): chapter, its progress, skips, play and speed.

struct PlayerMedium: View {
    let entry: Entry

    var body: some View {
        WidgetFrame(entry: entry) { state in
            HStack(spacing: 14) {
                Cover(image: entry.cover, size: 118)
                VStack(alignment: .leading, spacing: 3) {
                    Text(state.title)
                        .font(.system(size: 15, weight: .bold))
                        .foregroundStyle(Color.paper)
                        .lineLimit(1)
                    Text(state.chapterTitle ?? state.author ?? "")
                        .font(.system(size: 12))
                        .foregroundStyle(Color.dust)
                        .lineLimit(1)
                    Text(chapterLine(state))
                        .font(.system(size: 11, weight: .medium))
                        .foregroundStyle(Color.dust)
                        .lineLimit(1)
                    ProgressBar(progress: state.chapterProgress(at: entry.date) ?? state.bookProgress(at: entry.date))
                        .padding(.vertical, 3)
                    HStack(spacing: 0) {
                        SkipButton(forward: false, seconds: state.skipSeconds, size: 26)
                        Spacer(minLength: 4)
                        PlayButton(isPlaying: state.isPlaying, size: 38)
                        Spacer(minLength: 4)
                        SkipButton(forward: true, seconds: state.skipSeconds, size: 26)
                        Spacer(minLength: 8)
                        VStack(spacing: 2) {
                            SpeedStepButton(faster: true, width: 38, height: 16)
                            SpeedLabel(speed: state.speed, size: 11)
                            SpeedStepButton(faster: false, width: 38, height: 16)
                        }
                    }
                }
            }
        }
    }

    private func chapterLine(_ state: WidgetState) -> String {
        if let left = state.chapterRemaining(at: entry.date) {
            return WidgetState.formatLeft(left) + " in chapter"
        }
        return WidgetState.formatLeft(state.remaining(at: entry.date))
    }
}

struct PlayerWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationPlayer", provider: Provider()) { PlayerMedium(entry: $0) }
            .configurationDisplayName("Player")
            .description("The chapter, skip buttons, play and speed.")
            .supportedFamilies([.systemMedium])
    }
}

// MARK: Speed Deck (medium): a long, fine slider.

struct SpeedDeckMedium: View {
    let entry: Entry

    var body: some View {
        WidgetFrame(entry: entry) { state in
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 10) {
                    Cover(image: entry.cover, size: 50)
                    VStack(alignment: .leading, spacing: 2) {
                        Text(state.title)
                            .font(.system(size: 14, weight: .bold))
                            .foregroundStyle(Color.paper)
                            .lineLimit(1)
                        Text(WidgetState.formatLeft(state.remaining(at: entry.date)))
                            .font(.system(size: 12))
                            .foregroundStyle(Color.dust)
                    }
                    Spacer(minLength: 6)
                    PlayButton(isPlaying: state.isPlaying, size: 38)
                }
                Spacer(minLength: 6)
                HStack {
                    SpeedStepButton(faster: false, width: 40, height: 22)
                    Spacer()
                    SpeedLabel(speed: state.speed, size: 20)
                    Spacer()
                    SpeedStepButton(faster: true, width: 40, height: 22)
                }
                Spacer(minLength: 6)
                HorizontalSpeedSlider(speed: state.speed, stops: WidgetState.fineSpeedStops, barHeight: 12, labelSize: 9,
                    labelled: { $0 == $0.rounded() || $0 == 1.5 })
            }
        }
    }
}

struct SpeedDeckWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationSpeedDeck", provider: Provider()) { SpeedDeckMedium(entry: $0) }
            .configurationDisplayName("Speed Deck")
            .description("A long speed slider from 1x to 10x, with − and + for 0.1 steps.")
            .supportedFamilies([.systemMedium])
    }
}

// MARK: Full Player (large): everything, over the blurred cover, with recent books.

struct FullPlayerLarge: View {
    let entry: Entry

    var body: some View {
        WidgetFrame(entry: entry, content: { state in
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .top, spacing: 12) {
                    Cover(image: entry.cover, size: 96)
                    VStack(alignment: .leading, spacing: 3) {
                        Text(state.title)
                            .font(.system(size: 17, weight: .bold))
                            .foregroundStyle(Color.paper)
                            .lineLimit(2)
                        Text(state.author ?? "")
                            .font(.system(size: 13))
                            .foregroundStyle(Color.dust)
                            .lineLimit(1)
                        Text(WidgetState.formatLeft(state.remaining(at: entry.date)) + " at " + WidgetState.formatSpeed(state.speed))
                            .font(.system(size: 12, weight: .medium))
                            .foregroundStyle(Color.lamp)
                    }
                }
                if let chapter = state.chapterTitle {
                    Text(chapter)
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(Color.paper)
                        .lineLimit(1)
                }
                ProgressBar(progress: state.chapterProgress(at: entry.date) ?? state.bookProgress(at: entry.date), height: 5)
                if let left = state.chapterRemaining(at: entry.date) {
                    Text(WidgetState.formatLeft(left) + " in chapter")
                        .font(.system(size: 11))
                        .foregroundStyle(Color.dust)
                }
                HStack {
                    Spacer()
                    SkipButton(forward: false, seconds: state.skipSeconds, size: 32)
                    Spacer()
                    PlayButton(isPlaying: state.isPlaying, size: 52)
                    Spacer()
                    SkipButton(forward: true, seconds: state.skipSeconds, size: 32)
                    Spacer()
                }
                HStack(spacing: 6) {
                    SpeedStepButton(faster: false, width: 28, height: 20)
                    HorizontalSpeedSlider(speed: state.speed, stops: WidgetState.speedStops, barHeight: 8, labelSize: 10)
                    SpeedStepButton(faster: true, width: 28, height: 20)
                }
                if !entry.recent.isEmpty {
                    Divider().overlay(Color.white.opacity(0.15))
                    HStack(spacing: 10) {
                        ForEach(entry.recent.prefix(2), id: \.book) { recent in
                            Button(intent: OpenBookIntent(bookId: recent.book.bookId)) {
                                HStack(spacing: 6) {
                                    Cover(image: recent.cover, size: 28)
                                    Text(recent.book.title)
                                        .font(.system(size: 11, weight: .medium))
                                        .foregroundStyle(Color.paper)
                                        .lineLimit(1)
                                }
                                .frame(maxWidth: .infinity, alignment: .leading)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
        }, background: {
            ZStack {
                Color.ink
                if let cover = entry.cover {
                    Image(uiImage: cover).resizable().aspectRatio(contentMode: .fill).blur(radius: 30).opacity(0.55)
                }
                LinearGradient(colors: [Color.ink.opacity(0.35), Color.ink.opacity(0.85)], startPoint: .top, endPoint: .bottom)
            }
        })
    }
}

struct FullPlayerWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationFullPlayer", provider: Provider()) { FullPlayerLarge(entry: $0) }
            .configurationDisplayName("Full Player")
            .description("Chapter, skips, a speed slider and your recent books.")
            .supportedFamilies([.systemLarge])
    }
}
