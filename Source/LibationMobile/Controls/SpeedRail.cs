using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LibationMobile.Controls;

/// <summary>
/// A speed picker drawn as a row of ticks on a logarithmic scale, so the commonly used 1x-3x range gets most
/// of the width. Ticks up to the current speed are lit, warming in colour as the speed rises.
/// Drag or tap anywhere on the rail to set the speed. With <see cref="Logarithmic"/> off it is an even scale, ticked
/// every <see cref="TickStep"/>, as for syllables a second.
/// </summary>
public class SpeedRail : Control
{
	public static readonly StyledProperty<double> SpeedProperty =
		AvaloniaProperty.Register<SpeedRail, double>(nameof(Speed), 1, defaultBindingMode: BindingMode.TwoWay);

	public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<SpeedRail, double>(nameof(Minimum), 0.5);
	public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SpeedRail, double>(nameof(Maximum), 10);
	public static readonly StyledProperty<IReadOnlyList<double>> StopsProperty =
		AvaloniaProperty.Register<SpeedRail, IReadOnlyList<double>>(nameof(Stops), [1, 2, 3, 5, 10]);

	public static readonly StyledProperty<bool> LogarithmicProperty = AvaloniaProperty.Register<SpeedRail, bool>(nameof(Logarithmic), true);
	public static readonly StyledProperty<double> TickStepProperty = AvaloniaProperty.Register<SpeedRail, double>(nameof(TickStep), 1);
	public static readonly StyledProperty<string> UnitProperty = AvaloniaProperty.Register<SpeedRail, string>(nameof(Unit), "×");

	public double Speed { get => GetValue(SpeedProperty); set => SetValue(SpeedProperty, value); }
	public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
	public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
	/// <summary>Speeds drawn as long, labelled ticks.</summary>
	public IReadOnlyList<double> Stops { get => GetValue(StopsProperty); set => SetValue(StopsProperty, value); }
	/// <summary>A log scale for speeds; off, an even one.</summary>
	public bool Logarithmic { get => GetValue(LogarithmicProperty); set => SetValue(LogarithmicProperty, value); }
	/// <summary>On an even scale, the gap between ticks.</summary>
	public double TickStep { get => GetValue(TickStepProperty); set => SetValue(TickStepProperty, value); }
	/// <summary>After each stop's label.</summary>
	public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

	// Rail colours: unlit ticks, then lit ticks warming from paper at 1x through lamp at 3x to ember at the maximum.
	private static readonly Color Dust = Color.Parse("#9C95B0");
	private static readonly Color Paper = Color.Parse("#EFE7D6");
	private static readonly Color Lamp = Color.Parse("#F0B25A");
	private static readonly Color Ember = Color.Parse("#E8745A");

	private const double TICK_WIDTH = 3;
	private const double MINOR_HEIGHT = 18;
	private const double MAJOR_HEIGHT = 34;
	private const double LABEL_GAP = 8;
	private const double LABEL_SIZE = 13;
	private const double SIDE_PADDING = 12;

	static SpeedRail()
	{
		AffectsRender<SpeedRail>(SpeedProperty, MinimumProperty, MaximumProperty, StopsProperty, LogarithmicProperty, TickStepProperty, UnitProperty);
		FocusableProperty.OverrideDefaultValue<SpeedRail>(true);
	}

	protected override Size MeasureOverride(Size availableSize)
		=> new(double.IsInfinity(availableSize.Width) ? 320 : availableSize.Width, MAJOR_HEIGHT + LABEL_GAP + LABEL_SIZE * 1.4);

	/// <summary>0 at <see cref="Minimum"/> to 1 at <see cref="Maximum"/>, logarithmically unless <see cref="Logarithmic"/> is off.</summary>
	private double Fraction(double speed)
		=> Logarithmic ? Math.Log(speed / Minimum) / Math.Log(Maximum / Minimum) : (speed - Minimum) / (Maximum - Minimum);
	private double SpeedAt(double fraction)
		=> Logarithmic ? Minimum * Math.Pow(Maximum / Minimum, Math.Clamp(fraction, 0, 1)) : Minimum + (Maximum - Minimum) * Math.Clamp(fraction, 0, 1);
	private double XFor(double speed) => SIDE_PADDING + Fraction(speed) * (Bounds.Width - 2 * SIDE_PADDING);

	private IEnumerable<double> Ticks()
	{
		if (!Logarithmic)
		{
			for (var s = Math.Ceiling(Minimum / TickStep) * TickStep; s <= Maximum + 1e-9; s += TickStep)
				yield return s;
			yield break;
		}
		for (var s = Minimum; s < 3 - 1e-9; s += 0.25)
			yield return s;
		for (var s = 3.0; s <= Maximum + 1e-9; s += 0.5)
			yield return s;
	}

	private Color Heat(double speed)
	{
		// An even scale warms by how far along it is: paper, lamp two fifths of the way, ember at the end.
		if (!Logarithmic)
		{
			var f = Fraction(speed);
			return f <= 0.4 ? Blend(Paper, Lamp, f / 0.4) : Blend(Lamp, Ember, (f - 0.4) / 0.6);
		}
		if (speed <= 1)
			return Paper;
		if (speed <= 3)
			return Blend(Paper, Lamp, (speed - 1) / 2);
		return Blend(Lamp, Ember, (Fraction(speed) - Fraction(3)) / (1 - Fraction(3)));
	}

	private static Color Blend(Color a, Color b, double t)
	{
		t = Math.Clamp(t, 0, 1);
		return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
	}

	public override void Render(DrawingContext context)
	{
		// Hidden or not laid out yet (blind training hides it): nothing to draw, and the label sums below go negative.
		if (Bounds.Width < 40)
			return;
		var baseline = MAJOR_HEIGHT;
		var stops = Stops.ToHashSet();
		var unlit = new SolidColorBrush(Dust, 0.35);
		var typeface = new Typeface(FontFamily.Default);

		// Transparent hit area, so taps between ticks still register.
		context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

		foreach (var tick in Ticks())
		{
			var isStop = stops.Contains(Math.Round(tick, 2));
			var height = isStop ? MAJOR_HEIGHT : MINOR_HEIGHT;
			var x = XFor(tick);
			var brush = tick <= Speed + 1e-6 ? new SolidColorBrush(Heat(tick)) : unlit;
			context.FillRectangle(brush, new Rect(x - TICK_WIDTH / 2, baseline - height, TICK_WIDTH, height), (float)(TICK_WIDTH / 2));

			if (isStop)
			{
				var label = new FormattedText($"{tick:0.#}{Unit}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, LABEL_SIZE,
					new SolidColorBrush(Dust));
				context.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, Bounds.Width - label.Width), baseline + LABEL_GAP));
			}
		}

		// The exact speed, which usually falls between ticks.
		var markerX = XFor(Math.Clamp(Speed, Minimum, Maximum));
		context.FillRectangle(new SolidColorBrush(Heat(Speed)), new Rect(markerX - 2.5, 0, 5, baseline + 4), 2.5f);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		e.Pointer.Capture(this);
		SetFromPointer(e.GetPosition(this));
		e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		if (e.Pointer.Captured == this)
			SetFromPointer(e.GetPosition(this));
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		e.Pointer.Capture(null);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		var step = e.Key switch { Key.Left or Key.Down => -0.1, Key.Right or Key.Up => 0.1, _ => 0 };
		if (step != 0)
		{
			Speed = Math.Clamp(Math.Round((Speed + step) * 10) / 10, Minimum, Maximum);
			e.Handled = true;
		}
	}

	private void SetFromPointer(Point point)
	{
		var fraction = (point.X - SIDE_PADDING) / (Bounds.Width - 2 * SIDE_PADDING);
		var speed = SpeedAt(fraction);
		// Snap to the nearest labelled stop when close to it, otherwise to 0.1.
		var nearestStop = Stops.MinBy(s => Math.Abs(XFor(s) - point.X));
		Speed = Math.Abs(XFor(nearestStop) - point.X) < 10 ? nearestStop : Math.Round(speed * 10) / 10;
	}
}
