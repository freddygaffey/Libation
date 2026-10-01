import AppIntents
import SwiftUI
import UIKit
import WidgetKit

// The app's colours (App.axaml).
extension Color {
    static let ink = Color(red: 0x1D / 255, green: 0x1A / 255, blue: 0x2E / 255)
    static let slate = Color(red: 0x2A / 255, green: 0x26 / 255, blue: 0x40 / 255)
    static let paper = Color(red: 0xEF / 255, green: 0xE7 / 255, blue: 0xD6 / 255)
    static let dust = Color(red: 0x9C / 255, green: 0x95 / 255, blue: 0xB0 / 255)
    static let lamp = Color(red: 0xF0 / 255, green: 0xB2 / 255, blue: 0x5A / 255)
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
        .clipShape(RoundedRectangle(cornerRadius: size / 10))
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

struct SkipButton: View {
    let forward: Bool
    let seconds: Double?
    let size: CGFloat

    var body: some View {
        Button(intent: SkipIntent(forward: forward)) {
            ZStack {
                Image(systemName: forward ? "goforward" : "gobackward")
                    .font(.system(size: size * 0.8, weight: .regular))
                Text(WidgetState.formatSkip(seconds))
                    .font(.system(size: size * 0.28, weight: .bold))
                    .offset(y: size * 0.04)
            }
            .foregroundStyle(Color.paper)
            .frame(width: size, height: size)
        }
        .buttonStyle(.plain)
    }
}

/// A + or − speed button.
struct SpeedStepButton: View {
    let faster: Bool
    var fine = false
    var width: CGFloat = 44
    var height: CGFloat = 22

    var body: some View {
        Button(intent: ChangeSpeedIntent(faster: faster, fine: fine)) {
            Image(systemName: faster ? "plus" : "minus")
                .font(.system(size: height * 0.55, weight: .bold))
                .foregroundStyle(Color.paper)
                .frame(width: width, height: height)
                .background(Capsule().fill(Color.white.opacity(0.12)))
        }
        .buttonStyle(.plain)
    }
}

struct SpeedLabel: View {
    let speed: Double
    var size: CGFloat = 13

    var body: some View {
        HStack(spacing: 2) {
            Image(systemName: "gauge.with.dots.needle.67percent").font(.system(size: size * 0.85, weight: .semibold))
            Text(WidgetState.formatSpeed(speed)).font(.system(size: size, weight: .bold)).monospacedDigit()
        }
        .foregroundStyle(Color.lamp)
        .fixedSize()
    }
}

struct ProgressBar: View {
    let progress: Double
    var height: CGFloat = 4

    var body: some View {
        GeometryReader { geometry in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.white.opacity(0.15))
                Capsule().fill(Color.lamp).frame(width: max(height, geometry.size.width * progress))
            }
        }
        .frame(height: height)
    }
}

/// The notch at or below the speed: the one a slider marks as current.
func currentStop(_ stops: [Double], _ speed: Double) -> Double {
    stops.last { $0 <= speed + 0.001 } ?? stops[0]
}

/// A tap-to-set speed slider laid out across: a segment per notch, filled up to the current speed.
struct HorizontalSpeedSlider: View {
    let speed: Double
    let stops: [Double]
    var barHeight: CGFloat = 10
    var labelSize: CGFloat = 9
    var labelled: (Double) -> Bool = { _ in true }
    var tinted = false

    var body: some View {
        let current = currentStop(stops, speed)
        HStack(spacing: 2) {
            ForEach(stops, id: \.self) { stop in
                Button(intent: SetSpeedIntent(speed: stop)) {
                    VStack(spacing: 3) {
                        RoundedRectangle(cornerRadius: barHeight / 2)
                            .fill(fill(stop, current))
                            .frame(height: stop == current ? barHeight + 4 : barHeight)
                            .frame(height: barHeight + 4)
                        Text(labelled(stop) ? WidgetState.formatSpeed(stop) : " ")
                            .font(.system(size: labelSize, weight: stop == current ? .bold : .medium))
                            .foregroundStyle(stop == current ? (tinted ? Color.primary : Color.lamp) : (tinted ? Color.secondary : Color.dust))
                            .lineLimit(1)
                            .minimumScaleFactor(0.6)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
    }

    private func fill(_ stop: Double, _ current: Double) -> Color {
        if tinted { return stop <= current ? Color.primary : Color.primary.opacity(0.25) }
        return stop == current ? .lamp : stop < current ? .lamp.opacity(0.55) : .white.opacity(0.14)
    }
}

/// The same slider stood upright: fastest at the top.
struct VerticalSpeedSlider: View {
    let speed: Double
    let stops: [Double]

    var body: some View {
        let current = currentStop(stops, speed)
        VStack(spacing: 2) {
            ForEach(stops.reversed(), id: \.self) { stop in
                Button(intent: SetSpeedIntent(speed: stop)) {
                    HStack(spacing: 5) {
                        Text(WidgetState.formatSpeed(stop))
                            .font(.system(size: 11, weight: stop == current ? .heavy : .medium))
                            .monospacedDigit()
                            .foregroundStyle(stop == current ? Color.lamp : Color.dust)
                            .frame(width: 30, alignment: .trailing)
                        RoundedRectangle(cornerRadius: 4)
                            .fill(stop == current ? Color.lamp : stop < current ? Color.lamp.opacity(0.55) : Color.white.opacity(0.14))
                            .frame(width: stop == current ? 26 : 18)
                            .frame(width: 26, alignment: .leading)
                    }
                    .frame(maxHeight: .infinity)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
    }
}

func backgroundGradient() -> LinearGradient {
    LinearGradient(colors: [.slate, .ink], startPoint: .top, endPoint: .bottom)
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
