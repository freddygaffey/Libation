import SwiftUI
import WidgetKit

@main
struct LibationWidgets: WidgetBundle {
    var body: some Widget {
        NowPlayingWidget()
        SpeedDialWidget()
        SpeedBarWidget()
        PlayerWidget()
        SpeedDeckWidget()
        FullPlayerWidget()
        SpeedStripWidget()
        NowPlayingStripWidget()
        RingWidget()
        InlineWidget()
        if #available(iOS 18.0, *) {
            PlayControl()
            PauseControl()
            FasterControl()
            SlowerControl()
        }
    }
}
