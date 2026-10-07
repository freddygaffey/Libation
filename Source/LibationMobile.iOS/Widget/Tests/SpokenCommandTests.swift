// Tests for SpokenCommand.parse, run on the Mac:
//   xcrun swiftc Intents/SpokenCommand.swift Tests/SpokenCommandTests.swift -o /tmp/spoken && /tmp/spoken
// Each case is what Siri might hear, and what it should do.

import Foundation

@main
struct SpokenCommandTests {
    static func main() {
        let cases: [(String, SpokenCommand?)] = [
            ("seven point three", .setSpeed(7.3)), ("7.3", .setSpeed(7.3)), ("7.3x", .setSpeed(7.3)), ("set it to 7.1", .setSpeed(7.1)),
            ("Seven and a half", .setSpeed(7.5)), ("7 and a half", .setSpeed(7.5)), ("seven", .setSpeed(7)), ("7 times", .setSpeed(7)),
            ("ten", .setSpeed(10)), ("speed 6.5", .setSpeed(6.5)), ("set speed to six point eight", .setSpeed(6.8)), ("7½", .setSpeed(7.5)),
            ("seven three", .setSpeed(7.3)), ("go to 5", .setSpeed(5)), ("twelve", .setSpeed(10)), ("point five", .setSpeed(0.5)),
            ("faster", .change(0.5)), ("a bit faster", .change(0.5)), ("speed up", .change(0.5)), ("up by point two", .change(0.2)),
            ("faster by a tenth", .change(0.1)), ("increase by 0.1", .change(0.1)), ("plus 0.3", .change(0.3)), ("+0.5", .change(0.5)),
            ("slower", .change(-0.5)), ("slow down by a half", .change(-0.5)), ("decrease speed by point one", .change(-0.1)),
            ("minus 0.2", .change(-0.2)), ("down 0.1", .change(-0.1)), ("slower by one", .change(-1)), ("faster by 1.5", .change(1.5)),
            ("speed up to 8", .setSpeed(8)), ("speed up 2", .setSpeed(2)), ("a little slower", .change(-0.5)),
            ("pause", .pause), ("stop", .pause), ("hold on", .pause), ("play", .play), ("resume", .play), ("continue", .play),
            ("what's my speed", .current), ("what speed am I at", .current), ("how long is left", .current), ("how fast", .current),
            ("skip forward", .skip(forward: true)), ("go back", .skip(forward: false)), ("rewind", .skip(forward: false)),
            ("go faster", .change(0.5)), ("faster point two", .change(0.2)), ("up to 9", .setSpeed(9)), ("lower it to three", .setSpeed(3)),
            ("back 30 seconds", .skip(forward: false)), ("make it 6.9", .setSpeed(6.9)), ("Set Libation to 7.3.", .setSpeed(7.3)),
            ("eight point oh", .setSpeed(8)), ("seven dot two", .setSpeed(7.2)), ("1/2 faster", .change(0.5)), ("three quarters slower", .change(-0.75)),
            ("speed up by a fifth", .change(0.2)), ("down a tenth", .change(-0.1)), ("unpause", .play),
            ("seven and three quarters", .setSpeed(7.8)), ("one and a half faster", .change(1.5)), ("two tenths slower", .change(-0.2)),
            ("list my books", .list(5)), ("list my last 10 books", .list(10)), ("what books have I got", .list(5)),
            ("recent books", .list(5)), ("list the last three books", .list(3)),
            ("play number 3", .playNumber(3)), ("number two", .playNumber(2)), ("play the third one", .playNumber(3)),
            ("book 4", .playNumber(4)), ("play 2", .playNumber(2)), ("play the first", .playNumber(1)),
            ("play dune", .playNamed("dune")), ("play the book by andy weir", .playNamed("the book by andy weir")),
            ("listen to project hail mary", .playNamed("project hail mary")), ("play at 7", .setSpeed(7)),
            ("play faster", .change(0.5)), ("play", .play),
            ("banana", nil),
        ]
        var failed = 0
        for (heard, expected) in cases {
            let got = SpokenCommand.parse(heard)
            if got != expected { failed += 1; print("FAIL \"\(heard)\" -> \(String(describing: got)), expected \(String(describing: expected))   [\(SpokenCommand.normalise(heard))]") }
        }
        print("\(cases.count - failed) of \(cases.count) commands passed")

        // Finding a book from a few words.
        let books = [
            ListedBook(id: "1", title: "Dune", author: "Frank Herbert"),
            ListedBook(id: "2", title: "Project Hail Mary", author: "Andy Weir"),
            ListedBook(id: "3", title: "The Way of Kings", author: "Brandon Sanderson"),
            ListedBook(id: "4", title: "Ready Player One", author: "Ernest Cline"),
            ListedBook(id: "5", title: "The Martian", author: "Andy Weir"),
            ListedBook(id: "6", title: "A 4-Track Mind", author: "Radiolab"),
            ListedBook(id: "7", title: "Mistborn: The Final Empire", author: "Brandon Sanderson"),
        ]
        let finds: [(String, String?)] = [
            ("dune", "1"), ("Dune", "1"), ("project hail mary", "2"), ("hail mary", "2"), ("the sanderson one", "3"),
            ("way of kings", "3"), ("ready player one", "4"), ("the martian", "5"), ("andy weir", "2"),
            ("the martian by andy weir", "5"), ("mistborn", "7"), ("4 track mind", "6"), ("radiolab", "6"),
            ("frank herbert", "1"), ("sanderson mistborn", "7"), ("harry potter", nil), ("the", nil),
        ]
        var missed = 0
        for (query, expected) in finds {
            let got = BookMatcher.best(query, in: books)?.id
            if got != expected { missed += 1; print("FIND FAIL \"\(query)\" -> \(got ?? "nothing"), expected \(expected ?? "nothing")") }
        }
        print("\(finds.count - missed) of \(finds.count) books found")
        failed += missed
        if failed > 0 { exit(1) }
    }
}
