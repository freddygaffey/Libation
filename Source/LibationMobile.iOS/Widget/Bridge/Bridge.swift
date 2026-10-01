import WidgetKit

/// Called by the .NET app after it writes new state for the widget.
@_cdecl("libation_widget_reload")
public func libationWidgetReload() {
    if #available(iOS 14.0, *) {
        WidgetCenter.shared.reloadAllTimelines()
    }
}
