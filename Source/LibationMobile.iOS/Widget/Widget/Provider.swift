import SwiftUI
import UIKit
import WidgetKit

struct Entry: TimelineEntry {
    let date: Date
    let state: WidgetState?
    let cover: UIImage?
    var recent: [(book: RecentBook, cover: UIImage?)] = []
}

/// Shared by every widget: what the app last played, counting down while it plays.
struct Provider: TimelineProvider {
    func placeholder(in context: Context) -> Entry {
        Entry(date: Date(), state: WidgetState(bookId: nil, title: "Book title", author: "Author", remainingSeconds: 3 * 3600, speed: 2, isPlaying: false,
            updatedAt: Date().timeIntervalSince1970, durationSeconds: 8 * 3600, chapterTitle: "Chapter 4", chapterRemainingSeconds: 900, chapterDurationSeconds: 1800, skipSeconds: 30), cover: nil)
    }

    func getSnapshot(in context: Context, completion: @escaping (Entry) -> Void) {
        completion(WidgetState.load() == nil ? placeholder(in: context) : current(at: Date()))
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> Void) {
        let now = Date()
        let entry = current(at: now)
        // While playing, count down a minute at a time; the app redraws when anything else changes.
        guard entry.state?.isPlaying == true else {
            completion(Timeline(entries: [entry], policy: .never))
            return
        }
        let entries = (0..<60).map { minute in
            Entry(date: now.addingTimeInterval(Double(minute) * 60), state: entry.state, cover: entry.cover, recent: entry.recent)
        }
        completion(Timeline(entries: entries, policy: .atEnd))
    }

    private func current(at date: Date) -> Entry {
        let state = WidgetState.load()
        return Entry(date: date, state: state, cover: WidgetState.loadCover().flatMap(UIImage.init(data:)),
            recent: RecentBook.load().filter { $0.bookId != state?.bookId }.map { ($0, $0.cover.flatMap(UIImage.init(data:))) })
    }
}

/// Every widget's frame: its content when a book has been played, else a prompt, over the app's background.
struct WidgetFrame<Content: View, Background: View>: View {
    let entry: Entry
    @ViewBuilder let content: (WidgetState) -> Content
    @ViewBuilder var background: () -> Background
    @Environment(\.widgetFamily) private var family

    var body: some View {
        Group {
            if let state = entry.state {
                content(state)
            } else {
                NothingPlayedView()
            }
        }
        .containerBackground(for: .widget) { background() }
        .widgetURL(tapLink)
    }

    /// A small widget ignores links inside it, so its play button would only open the app. Instead a tap anywhere
    /// but the speed buttons plays, as the play button would.
    private var tapLink: URL? {
        family == .systemSmall && entry.state?.isPlaying == false ? WidgetLink.play : nil
    }
}

extension WidgetFrame where Background == LinearGradient {
    init(entry: Entry, @ViewBuilder content: @escaping (WidgetState) -> Content) {
        self.entry = entry
        self.content = content
        self.background = { backgroundGradient() }
    }
}
