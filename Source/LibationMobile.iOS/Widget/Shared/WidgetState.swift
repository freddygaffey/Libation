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

    static let minSpeed = 0.5
    static let maxSpeed = 10.0
    static let speedStep = 0.5

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

    /// Time left in real time at this speed, as it stands at `date`.
    func remaining(at date: Date) -> Double {
        let elapsed = isPlaying ? max(0, date.timeIntervalSince1970 - updatedAt) * speed : 0
        return max(0, remainingSeconds - elapsed) / speed
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

/// A request from the widget for the app: "play", "pause" or "speed".
struct WidgetCommand: Codable {
    var command: String
    var bookId: String?
    var speed: Double?
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

    static func write(_ account: String, _ data: Data) {
        let q = query(account)
        let update: [String: Any] = [kSecValueData as String: data]
        if SecItemUpdate(q as CFDictionary, update as CFDictionary) == errSecItemNotFound {
            var add = q
            add[kSecValueData as String] = data
            // The widget is drawn while the phone is locked.
            add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
            SecItemAdd(add as CFDictionary, nil)
        }
    }
}
