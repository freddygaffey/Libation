import Foundation

/// What a spoken request asks for, read from whatever words Siri heard: "seven point three", "7.3", "seven and a half",
/// "faster", "up by a tenth", "slower by point five", "minus 0.2", "pause", "what's my speed".
/// Plain Swift with no system frameworks, so it can be tested on the Mac (see Tests/SpokenCommandTests.swift).
enum SpokenCommand: Equatable {
    case setSpeed(Double)
    case change(Double)
    case pause
    case play
    case current
    case skip(forward: Bool)

    /// Speed up or slow down by this much when no amount is said.
    static let defaultChange = 0.5

    static func parse(_ heard: String) -> SpokenCommand? {
        let text = normalise(heard)
        let words = Set(text.split(separator: " ").map(String.init))
        func has(_ any: String...) -> Bool { any.contains { word in word.contains(" ") ? text.contains(word) : words.contains(word) } }

        // Questions first: "what speed am I at", "how fast", "how long is left".
        if has("what", "what's", "whats", "how", "current", "currently", "tell me") && !has("set", "make") {
            return .current
        }

        let number = firstNumber(in: text)
        let faster = has("faster", "quicker", "up", "increase", "raise", "more", "plus", "speed up", "accelerate", "boost")
        let slower = has("slower", "down", "decrease", "lower", "less", "minus", "slow", "reduce")

        if has("skip", "back", "forward", "rewind", "ahead", "behind") && !faster && !slower {
            return .skip(forward: has("forward", "ahead", "skip") && !has("back", "rewind", "behind"))
        }
        if has("pause", "stop", "hold", "wait", "quiet", "silence") { return .pause }
        if (faster || slower) && !has(" to ") && !text.hasPrefix("to ") {
            let amount = number ?? defaultChange
            // "Speed up to 7" sets, "speed up by 0.5" or "speed up 0.5" changes, small numbers are changes, and a
            // number before the word is an amount: "1.5 faster".
            let numberFirst = firstNumberIndex(in: text).map { at in
                text.split(separator: " ").firstIndex { directionWords.contains(String($0)) }.map { at < $0 } ?? false
            } ?? false
            if number != nil && !has("by") && !numberFirst && amount >= 1.5 && !has("plus", "minus") {
                return .setSpeed(clamp(amount))
            }
            return .change(faster && !slower || has("plus") ? amount : -amount)
        }
        if let number { return .setSpeed(clamp(number)) }
        if has("play", "resume", "continue", "start", "go", "unpause") { return .play }
        return nil
    }

    private static let directionWords: Set<String> = ["faster", "quicker", "up", "increase", "raise", "more", "plus", "accelerate",
        "boost", "slower", "down", "decrease", "lower", "less", "minus", "slow", "reduce", "speed"]

    /// Which word, counting from 0, is the first number.
    private static func firstNumberIndex(in text: String) -> Int? {
        text.split(separator: " ").firstIndex { Double($0) != nil }
    }

    static func clamp(_ speed: Double) -> Double { min(10, max(0.5, (speed * 10).rounded() / 10)) }

    // MARK: Reading numbers

    private static let units = ["zero": 0, "oh": 0, "one": 1, "two": 2, "three": 3, "four": 4, "five": 5, "six": 6, "seven": 7,
                                "eight": 8, "nine": 9, "ten": 10, "eleven": 11, "twelve": 12]

    /// Lower case, digits for number words, "and a half" as ".5", and nothing but letters, digits, points and spaces.
    static func normalise(_ heard: String) -> String {
        var text = " " + heard.lowercased() + " "
        let replacements: [(String, String)] = [
            ("½", " and a half "), ("¼", " and a quarter "), ("×", " x "), ("%", " "),
            ("1/2", " and a half "), ("1/4", " and a quarter "), ("3/4", " and three quarters "),
            ("+", " plus "), ("−", " minus "),
            ("’", "'"), ("-", " "), (",", " "), ("?", " "), ("!", " "),
            ("a bit", " "), ("a little", " "), ("little", " "), ("bit", " "), ("please", " "), ("can you", " "),
            ("the speed", " speed "), ("times", " x "),
        ]
        for (from, to) in replacements { text = text.replacingOccurrences(of: from, with: to) }
        // "1.5x" and "7x": keep the number, drop the x.
        text = text.replacingOccurrences(of: #"(\d)\s*x\b"#, with: "$1", options: .regularExpression)
        // A minus sign in front of a number: "-0.5" was turned into " 0.5" above; "minus" is said instead.
        text = text.replacingOccurrences(of: #"[^a-z0-9.' ]"#, with: " ", options: .regularExpression)

        // Fractions said with number words, before those words become digits: "three quarters", "two tenths".
        let wordFractions: [(String, String)] = [
            (" one and a half ", " 1.5 "), (" one half ", " 0.5 "), (" one tenth ", " 0.1 "), (" two tenths ", " 0.2 "),
            (" three tenths ", " 0.3 "), (" one fifth ", " 0.2 "), (" three quarters ", " 0.75 "), (" one quarter ", " 0.25 "),
        ]
        for (from, to) in wordFractions { text = text.replacingOccurrences(of: from, with: to) }
        // Fractions on their own, after "7 and a half" has been read: "a tenth", "a half", "half".
        let fractions: [(String, String)] = [
            (" a half ", " 0.5 "), (" half ", " 0.5 "), (" a tenth ", " 0.1 "), (" tenth ", " 0.1 "),
            (" a fifth ", " 0.2 "), (" a quarter ", " 0.25 "), (" quarter ", " 0.25 "),
        ]

        // Number words to digits, a word at a time, so "seven point three" becomes "7 point 3".
        var tokens = text.split(separator: " ").map(String.init)
        tokens = tokens.map { units[$0].map(String.init) ?? $0 }
        text = " " + tokens.joined(separator: " ") + " "
        // "7 and a half" and "7 and a quarter".
        text = text.replacingOccurrences(of: #" (\d+) and a half "#, with: " $1.5 ", options: .regularExpression)
        text = text.replacingOccurrences(of: #" (\d+) and a quarter "#, with: " $1.25 ", options: .regularExpression)
        text = text.replacingOccurrences(of: #" (\d+) and 0\.75 "#, with: " $1.75 ", options: .regularExpression)
        for (from, to) in fractions { text = text.replacingOccurrences(of: from, with: to) }
        // "7 point 3", "point 5", "7 dot 3".
        text = text.replacingOccurrences(of: #" (\d+) (?:point|dot|decimal) (\d+)"#, with: " $1.$2", options: .regularExpression)
        text = text.replacingOccurrences(of: #" (?:point|dot) (\d+)"#, with: " 0.$1", options: .regularExpression)
        text = text.replacingOccurrences(of: #" \.(\d)"#, with: " 0.$1", options: .regularExpression)
        // "7 3" said as two digits means 7.3, but only for a single digit after a whole speed.
        text = text.replacingOccurrences(of: #" ([1-9]|10) ([0-9]) (?!\d)"#, with: " $1.$2 ", options: .regularExpression)
        return text.replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression).trimmingCharacters(in: .whitespaces)
    }

    static func firstNumber(in text: String) -> Double? {
        guard let match = text.range(of: #"\d+(?:\.\d+)?"#, options: .regularExpression) else { return nil }
        return Double(text[match])
    }
}
