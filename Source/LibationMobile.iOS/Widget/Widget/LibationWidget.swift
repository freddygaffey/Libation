import SwiftUI
import UIKit
import WidgetKit

// The app's colours (App.axaml).
private extension Color {
    static let ink = Color(red: 0x1D / 255, green: 0x1A / 255, blue: 0x2E / 255)
    static let slate = Color(red: 0x2A / 255, green: 0x26 / 255, blue: 0x40 / 255)
    static let paper = Color(red: 0xEF / 255, green: 0xE7 / 255, blue: 0xD6 / 255)
    static let dust = Color(red: 0x9C / 255, green: 0x95 / 255, blue: 0xB0 / 255)
    static let lamp = Color(red: 0xF0 / 255, green: 0xB2 / 255, blue: 0x5A / 255)
}

struct Entry: TimelineEntry {
    let date: Date
    let state: WidgetState?
    let cover: UIImage?
}

struct Provider: TimelineProvider {
    func placeholder(in context: Context) -> Entry {
        Entry(date: Date(), state: WidgetState(bookId: nil, title: "Book title", author: "Author", remainingSeconds: 3 * 3600, speed: 2, isPlaying: false, updatedAt: Date().timeIntervalSince1970), cover: nil)
    }

    func getSnapshot(in context: Context, completion: @escaping (Entry) -> Void) {
        completion(current(at: Date()))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> Void) {
        let now = Date()
        let entry = current(at: now)
        // While playing, count the time left down a minute at a time; the app redraws when anything else changes.
        guard entry.state?.isPlaying == true else {
            completion(Timeline(entries: [entry], policy: .never))
            return
        }
        let entries = (0..<60).map { minute in
            Entry(date: now.addingTimeInterval(Double(minute) * 60), state: entry.state, cover: entry.cover)
        }
        completion(Timeline(entries: entries, policy: .atEnd))
    }

    private func current(at date: Date) -> Entry {
        Entry(date: date, state: WidgetState.load(), cover: WidgetState.loadCover().flatMap(UIImage.init(data:)))
    }
}

struct Cover: View {
    let image: UIImage?
    let size: CGFloat

    var body: some View {
        Group {
            if let image {
                Image(uiImage: image).resizable().aspectRatio(contentMode: .fill)
            } else {
                ZStack {
                    Color.slate
                    Image(systemName: "headphones").foregroundStyle(Color.dust)
                }
            }
        }
        .frame(width: size, height: size)
        .clipShape(RoundedRectangle(cornerRadius: 6))
    }
}

struct PlayButton: View {
    let isPlaying: Bool
    let size: CGFloat

    var body: some View {
        Group {
            if isPlaying {
                Button(intent: PauseIntent()) { face }
            } else {
                Button(intent: PlayIntent()) { face }
            }
        }
        .buttonStyle(.plain)
    }

    private var face: some View {
        ZStack {
            Circle().fill(Color.paper)
            Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                .font(.system(size: size * 0.4, weight: .bold))
                .foregroundStyle(Color.ink)
                // A triangle looks off-centre when centred by its box.
                .offset(x: isPlaying ? 0 : size * 0.04)
        }
        .frame(width: size, height: size)
    }
}

/// Faster above, slower below, the speed between.
struct SpeedControl: View {
    let speed: Double

    var body: some View {
        VStack(spacing: 2) {
            stepButton(faster: true)
            HStack(spacing: 2) {
                Image(systemName: "gauge.with.dots.needle.67percent").font(.system(size: 11, weight: .semibold))
                Text(WidgetState.formatSpeed(speed)).font(.system(size: 13, weight: .bold)).monospacedDigit()
            }
            .foregroundStyle(Color.lamp)
            .fixedSize()
            stepButton(faster: false)
        }
    }

    private func stepButton(faster: Bool) -> some View {
        Button(intent: ChangeSpeedIntent(faster: faster)) {
            Image(systemName: faster ? "plus" : "minus")
                .font(.system(size: 12, weight: .bold))
                .foregroundStyle(Color.paper)
                .frame(width: 44, height: 22)
                .background(Capsule().fill(Color.white.opacity(0.12)))
        }
        .buttonStyle(.plain)
    }
}

struct SmallView: View {
    let entry: Entry

    var body: some View {
        if let state = entry.state {
            VStack(alignment: .leading, spacing: 0) {
                HStack(alignment: .top) {
                    Cover(image: entry.cover, size: 58)
                    Spacer(minLength: 4)
                    SpeedControl(speed: state.speed)
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
        } else {
            NothingPlayedView()
        }
    }
}

struct MediumView: View {
    let entry: Entry

    var body: some View {
        if let state = entry.state {
            HStack(spacing: 14) {
                Cover(image: entry.cover, size: 118)
                VStack(alignment: .leading, spacing: 4) {
                    Text(state.title)
                        .font(.system(size: 16, weight: .bold))
                        .foregroundStyle(Color.paper)
                        .lineLimit(2)
                    Text(state.author ?? "")
                        .font(.system(size: 13))
                        .foregroundStyle(Color.dust)
                        .lineLimit(1)
                    Spacer(minLength: 4)
                    HStack(spacing: 10) {
                        PlayButton(isPlaying: state.isPlaying, size: 40)
                        Text(WidgetState.formatLeft(state.remaining(at: entry.date)))
                            .font(.system(size: 13, weight: .medium))
                            .foregroundStyle(Color.dust)
                            .lineLimit(1)
                        Spacer(minLength: 0)
                        SpeedControl(speed: state.speed)
                    }
                }
            }
        } else {
            NothingPlayedView()
        }
    }
}

struct NothingPlayedView: View {
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Image(systemName: "headphones").font(.title2).foregroundStyle(Color.lamp)
            Text("Play a book in Libation to see it here.")
                .font(.system(size: 13))
                .foregroundStyle(Color.paper)
        }
    }
}

struct LibationWidgetView: View {
    @Environment(\.widgetFamily) var family
    let entry: Entry

    var body: some View {
        Group {
            if family == .systemMedium {
                MediumView(entry: entry)
            } else {
                SmallView(entry: entry)
            }
        }
        .containerBackground(for: .widget) {
            LinearGradient(colors: [.slate, .ink], startPoint: .top, endPoint: .bottom)
        }
    }
}

@main
struct LibationWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "LibationNowPlaying", provider: Provider()) { entry in
            LibationWidgetView(entry: entry)
        }
        .configurationDisplayName("Now playing")
        .description("The book you last played, with play and speed.")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}
