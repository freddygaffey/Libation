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
            ("banana", nil),
        ]
        var failed = 0
        for (heard, expected) in cases {
            let got = SpokenCommand.parse(heard)
            if got != expected { failed += 1; print("FAIL \"\(heard)\" -> \(String(describing: got)), expected \(String(describing: expected))   [\(SpokenCommand.normalise(heard))]") }
        }
        print("\(cases.count - failed) of \(cases.count) passed")
        if failed > 0 { exit(1) }
    }
}
