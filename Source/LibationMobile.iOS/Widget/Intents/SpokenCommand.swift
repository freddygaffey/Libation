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
    /// "List my last 5 books".
    case list(Int)
    /// "Play number 3", "play the second one": from the list, counting from 1.
    case playNumber(Int)
    /// "Play Dune", "play the one by Andy Weir": the words to look for.
    case playNamed(String)

    /// Speed up or slow down by this much when no amount is said.
    static let defaultChange = 0.5

    static func parse(_ heard: String) -> SpokenCommand? {
        let text = normalise(heard)
        let words = Set(text.split(separator: " ").map(String.init))
        func has(_ any: String...) -> Bool { any.contains { word in word.contains(" ") ? text.contains(word) : words.contains(word) } }

        // The books: "list my books", "what books have I got", "my last 10 books".
        if has("list", "books", "recent", "library") && !has("play") {
            return .list(Int(firstNumber(in: text) ?? 5).clamped(1, 20))
        }
        // A book by number: "play number 3", "number 3", "play the third", "book 2".
        if let n = bookNumber(in: text) { return .playNumber(n) }
        // A book by name: "play Dune", "play the book by Andy Weir". Not "play at 7" or "play faster".
        if let named = bookName(in: text) { return .playNamed(named) }

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

    private static let ordinals = ["first": 1, "second": 2, "third": 3, "fourth": 4, "fifth": 5, "sixth": 6, "seventh": 7,
        "eighth": 8, "ninth": 9, "tenth": 10, "1st": 1, "2nd": 2, "3rd": 3, "4th": 4, "5th": 5, "6th": 6, "7th": 7, "8th": 8,
        "9th": 9, "10th": 10, "last": 1, "latest": 1]

    private static func bookNumber(in text: String) -> Int? {
        if let match = text.range(of: #"\b(?:number|book|no|item|track) (\d{1,2})\b(?!\.)"#, options: .regularExpression),
           let n = Int(text[match].split(separator: " ").last!) {
            return n.clamped(1, 100)
        }
        // "play the third", "the second one", "play 3": but "play 3" only as a whole, so "play at 3" stays a speed.
        let words = text.split(separator: " ").map(String.init)
        if words.first == "play" || words.contains("one") || words.first == "the" {
            for word in words { if let n = ordinals[word], !text.contains(" tenth") || word != "tenth" { return n } }
        }
        if words.count == 2, words[0] == "play", let n = Int(words[1]) { return n.clamped(1, 100) }
        return nil
    }

    /// Words that ask for something other than a book after "play".
    private static let notBookWords: Set<String> = ["at", "faster", "slower", "speed", "x", "it", "on", "again", "music", "something",
        "resume", "please", "now", "back", "forward", "up", "down"]

    private static func bookName(in text: String) -> String? {
        guard text.hasPrefix("play ") || text.hasPrefix("listen to ") || text.hasPrefix("put on ") || text.hasPrefix("open ") else { return nil }
        var rest = text
        for prefix in ["play ", "listen to ", "put on ", "open "] where rest.hasPrefix(prefix) { rest = String(rest.dropFirst(prefix.count)) }
        let words = rest.split(separator: " ").map(String.init)
        if words.isEmpty || words.allSatisfy({ notBookWords.contains($0) || Double($0) != nil }) { return nil }
        if words.contains(where: { $0 == "at" || $0 == "speed" }) && firstNumber(in: rest) != nil { return nil }
        return rest
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

extension Int {
    func clamped(_ low: Int, _ high: Int) -> Int { Swift.min(high, Swift.max(low, self)) }
}

/// A book Siri can play, as the app lists it in the shared keychain ("books"), last listened first.
struct ListedBook: Codable, Equatable {
    var id: String
    var title: String
    var author: String?
}

/// Finds the book meant by a few spoken words: a title, an author, or both ("Dune", "the Sanderson one",
/// "Project Hail Mary by Andy Weir"). Words in the title count most, then the author's; a word can match the start
/// of a longer one ("sanders" for "Sanderson"). Among equal matches, the one listened to last wins.
enum BookMatcher {
    private static let ignored: Set<String> = ["the", "a", "an", "of", "book", "audiobook", "audio", "one", "1", "play", "please", "my",
        "in", "called", "named", "episode", "podcast", "novel", "series", "that", "this", "and", "to", "on", "for"]

    static func words(_ text: String) -> [String] {
        SpokenCommand.normalise(text).split(separator: " ").map(String.init).filter { !ignored.contains($0) && $0 != "by" }
    }

    static func best(_ query: String, in books: [ListedBook]) -> ListedBook? {
        let normalised = SpokenCommand.normalise(query)
        // "X by Y": X is the title, Y the author.
        let parts = normalised.components(separatedBy: " by ")
        let titleWords = words(parts[0])
        let authorWords = parts.count > 1 ? words(parts[1...].joined(separator: " ")) : []
        let all = titleWords + authorWords
        guard !all.isEmpty else { return nil }

        func matches(_ word: String, _ in: [String]) -> Double {
            if `in`.contains(word) { return 1 }
            if word.count >= 4 && `in`.contains(where: { $0.hasPrefix(word) || (word.hasPrefix($0) && $0.count >= 4) }) { return 0.7 }
            return 0
        }

        var best: (book: ListedBook, score: Double)?
        for book in books {
            let title = words(book.title)
            let author = words(book.author ?? "")
            var score = 0.0
            for word in titleWords { score += max(matches(word, title) * 2, matches(word, author) * 1.5) }
            for word in authorWords { score += max(matches(word, author) * 2, matches(word, title) * 0.5) }
            // Most of what was said must match.
            let possible = Double(all.count) * 2
            guard score >= possible * 0.5 else { continue }
            if best == nil || score > best!.score { best = (book, score) }
        }
        return best?.book
    }
}
