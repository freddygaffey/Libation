using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace LibationMobile.Controls;

/// <summary>
/// The clip being made, over a waveform of the audio around it, as in Audible's clip editor. Drag a handle to move that
/// end, or the middle to move the whole clip. The clip stays between <see cref="MinimumLength"/> and
/// <see cref="MaximumLength"/> seconds. A line marks the playhead while the clip is previewed. All times are seconds
/// into the book.
/// </summary>
public class ClipRange : Control
{
	public static readonly StyledProperty<double> WindowStartProperty = AvaloniaProperty.Register<ClipRange, double>(nameof(WindowStart));
	public static readonly StyledProperty<double> WindowEndProperty = AvaloniaProperty.Register<ClipRange, double>(nameof(WindowEnd), 120);
	public static readonly StyledProperty<double> StartProperty =
		AvaloniaProperty.Register<ClipRange, double>(nameof(Start), defaultBindingMode: BindingMode.TwoWay);
	public static readonly StyledProperty<double> EndProperty =
		AvaloniaProperty.Register<ClipRange, double>(nameof(End), 30, defaultBindingMode: BindingMode.TwoWay);
	public static readonly StyledProperty<double> PlayheadProperty = AvaloniaProperty.Register<ClipRange, double>(nameof(Playhead), double.NaN);
	public static readonly StyledProperty<IReadOnlyList<float>?> PeaksProperty = AvaloniaProperty.Register<ClipRange, IReadOnlyList<float>?>(nameof(Peaks));
	public static readonly StyledProperty<double> MinimumLengthProperty = AvaloniaProperty.Register<ClipRange, double>(nameof(MinimumLength), 1);
	public static readonly StyledProperty<double> MaximumLengthProperty = AvaloniaProperty.Register<ClipRange, double>(nameof(MaximumLength), 45);

	public double WindowStart { get => GetValue(WindowStartProperty); set => SetValue(WindowStartProperty, value); }
	public double WindowEnd { get => GetValue(WindowEndProperty); set => SetValue(WindowEndProperty, value); }
	public double Start { get => GetValue(StartProperty); set => SetValue(StartProperty, value); }
	public double End { get => GetValue(EndProperty); set => SetValue(EndProperty, value); }
	/// <summary>Where preview playback is; NaN hides the line.</summary>
	public double Playhead { get => GetValue(PlayheadProperty); set => SetValue(PlayheadProperty, value); }
	/// <summary>Loudness across the window, 0 to 1, evenly spaced. Null draws a plain track until it is loaded.</summary>
	public IReadOnlyList<float>? Peaks { get => GetValue(PeaksProperty); set => SetValue(PeaksProperty, value); }
	public double MinimumLength { get => GetValue(MinimumLengthProperty); set => SetValue(MinimumLengthProperty, value); }
	public double MaximumLength { get => GetValue(MaximumLengthProperty); set => SetValue(MaximumLengthProperty, value); }

	private static readonly Color Dust = Color.Parse("#9C95B0");
	private static readonly Color Lamp = Color.Parse("#F0B25A");
	private static readonly Color Paper = Color.Parse("#EFE7D6");
	private static readonly Color Ink = Color.Parse("#1E1B2E");

	private const double HANDLE_WIDTH = 14;
	/// <summary>How close a touch must be to a handle to take it, in pixels: fingers are wide.</summary>
	private const double HANDLE_REACH = 28;
	private const double BAR_GAP = 1;

	private enum Drag { None, Start, End, Both }
	private Drag dragging;
	private double dragFrom;
	private double startAtDrag, endAtDrag;

	static ClipRange()
	{
		AffectsRender<ClipRange>(WindowStartProperty, WindowEndProperty, StartProperty, EndProperty, PlayheadProperty, PeaksProperty);
	}

	public ClipRange()
	{
		ClipToBounds = false;
		Cursor = new Cursor(StandardCursorType.SizeWestEast);
	}

	protected override Size MeasureOverride(Size availableSize)
		=> new(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, 96);

	private double Span => Math.Max(1, WindowEnd - WindowStart);
	private double X(double seconds) => HANDLE_WIDTH + (seconds - WindowStart) / Span * (Bounds.Width - 2 * HANDLE_WIDTH);
	private double Seconds(double x) => WindowStart + (x - HANDLE_WIDTH) / Math.Max(1, Bounds.Width - 2 * HANDLE_WIDTH) * Span;

	public override void Render(DrawingContext context)
	{
		var height = Bounds.Height;
		var top = 6.0;
		var bottom = height - 6;
		var middle = (top + bottom) / 2;
		var startX = X(Start);
		var endX = X(End);

		// The clip's band behind the waveform.
		context.FillRectangle(new SolidColorBrush(Lamp, 0.16), new Rect(startX, 0, Math.Max(0, endX - startX), height), 6);

		// The waveform: bars, lit inside the clip.
		var peaks = Peaks;
		var width = Bounds.Width - 2 * HANDLE_WIDTH;
		var count = peaks?.Count ?? 0;
		if (count > 0)
		{
			var barWidth = Math.Max(1, width / count - BAR_GAP);
			var inside = new SolidColorBrush(Lamp);
			var outside = new SolidColorBrush(Dust, 0.45);
			for (var i = 0; i < count; i++)
			{
				var x = HANDLE_WIDTH + i * width / count;
				var half = Math.Max(1, peaks![i] * (bottom - top) / 2);
				var lit = x + barWidth / 2 >= startX && x + barWidth / 2 <= endX;
				context.FillRectangle(lit ? inside : outside, new Rect(x, middle - half, barWidth, half * 2), (float)Math.Min(1, barWidth / 2));
			}
		}
		else
		{
			context.FillRectangle(new SolidColorBrush(Dust, 0.3), new Rect(HANDLE_WIDTH, middle - 1, width, 2));
		}

		// Playhead.
		if (!double.IsNaN(Playhead) && Playhead >= WindowStart && Playhead <= WindowEnd)
			context.FillRectangle(new SolidColorBrush(Paper), new Rect(X(Playhead) - 1, 0, 2, height));

		// Handles: tall rounded tabs with a grip line, either side of the clip.
		DrawHandle(context, startX - HANDLE_WIDTH, height);
		DrawHandle(context, endX, height);
		context.FillRectangle(new SolidColorBrush(Lamp), new Rect(startX, 0, Math.Max(0, endX - startX), 2));
		context.FillRectangle(new SolidColorBrush(Lamp), new Rect(startX, height - 2, Math.Max(0, endX - startX), 2));
	}

	private static void DrawHandle(DrawingContext context, double x, double height)
	{
		context.FillRectangle(new SolidColorBrush(Lamp), new Rect(x, 0, HANDLE_WIDTH, height), 4);
		context.FillRectangle(new SolidColorBrush(Ink, 0.7), new Rect(x + HANDLE_WIDTH / 2 - 1, height / 2 - 10, 2, 20), 1);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		var x = e.GetPosition(this).X;
		var toStart = Math.Abs(x - (X(Start) - HANDLE_WIDTH / 2));
		var toEnd = Math.Abs(x - (X(End) + HANDLE_WIDTH / 2));
		dragging = toStart <= HANDLE_REACH && toStart <= toEnd ? Drag.Start
			: toEnd <= HANDLE_REACH ? Drag.End
			: x > X(Start) && x < X(End) ? Drag.Both
			: Drag.None;
		if (dragging == Drag.None)
			return;
		dragFrom = x;
		startAtDrag = Start;
		endAtDrag = End;
		e.Pointer.Capture(this);
		e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		if (dragging == Drag.None)
			return;
		var delta = Seconds(e.GetPosition(this).X) - Seconds(dragFrom);
		var limitLow = Math.Max(0, WindowStart);
		switch (dragging)
		{
			case Drag.Start:
				Start = Round(Math.Clamp(startAtDrag + delta, Math.Max(limitLow, End - MaximumLength), End - MinimumLength));
				break;
			case Drag.End:
				End = Round(Math.Clamp(endAtDrag + delta, Start + MinimumLength, Math.Min(WindowEnd, Start + MaximumLength)));
				break;
			case Drag.Both:
				// One rounded shift for both ends, so moving the clip never changes its length.
				var shift = Math.Clamp(Round(delta), limitLow - startAtDrag, WindowEnd - endAtDrag);
				Start = startAtDrag + shift;
				End = endAtDrag + shift;
				break;
		}
		e.Handled = true;
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		dragging = Drag.None;
		e.Pointer.Capture(null);
	}

	protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
	{
		base.OnPointerCaptureLost(e);
		dragging = Drag.None;
	}

	/// <summary>Tenths of a second: finer than anyone can place a finger, coarse enough to read.</summary>
	private static double Round(double seconds) => Math.Round(seconds * 10) / 10;
}
