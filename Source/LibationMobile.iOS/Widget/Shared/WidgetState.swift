import Foundation
import Security

/// What the app last played, written by the app for the widget to show. Kept in the team's shared keychain
/// group; the item names and JSON fields match HomeWidget.cs and AppleHomeWidget.cs in the app.
struct WidgetState: Codable {
    var bookId: String?
    var title: String
    var author: String?
    /// Book time left, at 1x.
    var remainingSeconds: Double
    var speed: Double
    var isPlaying: Bool
    /// Seconds since 1970 when this was written; while playing, the time left counts down from here.
    var updatedAt: Double
    /// The whole book, at 1x.
    var durationSeconds: Double?
    var chapterTitle: String?
    /// Book time left in the chapter, and its length, at 1x.
    var chapterRemainingSeconds: Double?
    var chapterDurationSeconds: Double?
    /// The app's skip length, for the skip buttons' labels.
    var skipSeconds: Double?

    static let minSpeed = 0.5
    static let maxSpeed = 10.0
    static let speedStep = 0.5
    static let fineSpeedStep = 0.1
    /// The sliders' notches: the app's own speed stops.
    static let speedStops: [Double] = [1, 1.5, 2, 3, 5, 10]
    /// The wide slider has room for more.
    static let fineSpeedStops: [Double] = [1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.5, 4, 5, 6, 8, 10]

    /// Darwin notifications telling the app, if it is running, to read what the widget asked for.
    static let commandPosted = "io.github.freddygaffey.libation.widget.command"

    static func load() -> WidgetState? {
        SharedKeychain.read("state").flatMap { try? JSONDecoder().decode(WidgetState.self, from: $0) }
    }

    func save() {
        if let data = try? JSONEncoder().encode(self) {
            SharedKeychain.write("state", data)
        }
    }

    static func loadCover() -> Data? { SharedKeychain.read("cover") }

    static func formatSkip(_ seconds: Double?) -> String { "\(Int(seconds ?? 30))" }

    /// Time left in real time at this speed, as it stands at `date`.
    func remaining(at date: Date) -> Double {
        let elapsed = isPlaying ? max(0, date.timeIntervalSince1970 - updatedAt) * speed : 0
        return max(0, remainingSeconds - elapsed) / speed
    }

    /// Book time played since this was written.
    private func played(at date: Date) -> Double {
        isPlaying ? max(0, date.timeIntervalSince1970 - updatedAt) * speed : 0
    }

    /// Time left in the chapter in real time at this speed.
    func chapterRemaining(at date: Date) -> Double? {
        chapterRemainingSeconds.map { max(0, $0 - played(at: date)) / speed }
    }

    /// How far through the chapter, 0 to 1.
    func chapterProgress(at date: Date) -> Double? {
        guard let left = chapterRemainingSeconds, let length = chapterDurationSeconds, length > 0 else { return nil }
        return min(1, max(0, 1 - (left - played(at: date)) / length))
    }

    /// How far through the book, 0 to 1.
    func bookProgress(at date: Date) -> Double {
        guard let length = durationSeconds, length > 0 else { return 0 }
        return min(1, max(0, 1 - (remainingSeconds - played(at: date)) / length))
    }

    /// Settles the book time played so far, so the countdown carries on right after a change of speed or a pause.
    mutating func settle(at date: Date) {
        remainingSeconds = remaining(at: date) * speed
        updatedAt = date.timeIntervalSince1970
    }

    /// Like Audible: "3h 12m left", "12m left".
    static func formatLeft(_ seconds: Double) -> String {
        let minutes = Int(seconds / 60)
        return minutes >= 60 ? "\(minutes / 60)h \(minutes % 60)m left" : "\(minutes)m left"
    }

    static func formatSpeed(_ speed: Double) -> String {
        let rounded = (speed * 10).rounded() / 10
        return rounded == rounded.rounded() ? "\(Int(rounded))×" : String(format: "%.1f×", rounded)
    }
}

/// A book played before the current one, for the large widget.
struct RecentBook: Codable, Hashable {
    var bookId: String
    var title: String

    static func load() -> [RecentBook] {
        SharedKeychain.read("recent").flatMap { try? JSONDecoder().decode([RecentBook].self, from: $0) } ?? []
    }

    var cover: Data? { SharedKeychain.read("cover-" + bookId) }
}

/// Links that open the app, for buttons that need it on screen. The scheme is registered in the app's Info.plist
/// and matches HomeWidget.LINK_SCHEME.
enum WidgetLink {
    static let play = URL(string: "libation-player://play")!
    /// Open a book and play it.
    static func play(_ bookId: String) -> URL {
        URL(string: "libation-player://play/" + (bookId.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? bookId))!
    }

    static func open(_ bookId: String) -> URL {
        URL(string: "libation-player://open/" + (bookId.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? bookId))!
    }
}

/// A request from the widget for the app: "play", "pause", "speed", "skip" or "open".
struct WidgetCommand: Codable {
    var command: String
    var bookId: String?
    var speed: Double?
    /// For "skip": forwards or back.
    var forward: Bool?
    var at: Double

    static func post(_ command: WidgetCommand) {
        if let data = try? JSONEncoder().encode(command) {
            SharedKeychain.write("command", data)
        }
        CFNotificationCenterPostNotification(CFNotificationCenterGetDarwinNotifyCenter(),
            CFNotificationName(WidgetState.commandPosted as CFString), nil, nil, true)
    }
}

enum SharedKeychain {
    private static let service = "io.github.freddygaffey.libation.widget"

    private static var group: String? {
        Bundle.main.object(forInfoDictionaryKey: "LibationKeychainGroup") as? String
    }

    private static func query(_ account: String) -> [String: Any] {
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        if let group { query[kSecAttrAccessGroup as String] = group }
        return query
    }

    static func read(_ account: String) -> Data? {
        var q = query(account)
        q[kSecReturnData as String] = true
        q[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: AnyObject?
        return SecItemCopyMatching(q as CFDictionary, &result) == errSecSuccess ? result as? Data : nil
    }

    @discardableResult
    static func write(_ account: String, _ data: Data) -> OSStatus {
        let q = query(account)
        let update: [String: Any] = [kSecValueData as String: data]
        var status = SecItemUpdate(q as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var add = q
            add[kSecValueData as String] = data
            // The widget is drawn while the phone is locked.
            add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
            status = SecItemAdd(add as CFDictionary, nil)
        }
        return status
    }
}
