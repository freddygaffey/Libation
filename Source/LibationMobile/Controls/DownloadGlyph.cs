using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LibationMobile.ViewModels;
using System;

namespace LibationMobile.Controls;

/// <summary>
/// A book's download state, drawn like a store app's: an outlined arrow to download, a ring that fills while
/// downloading (with a stop square to cancel), and a solid check once the book is on the device.
/// </summary>
public class DownloadGlyph : Control
{
	public static readonly StyledProperty<DownloadState> StateProperty = AvaloniaProperty.Register<DownloadGlyph, DownloadState>(nameof(State));
	public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<DownloadGlyph, double>(nameof(Progress));

	public DownloadState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
	/// <summary>0 to 1, while <see cref="State"/> is <see cref="DownloadState.Downloading"/>.</summary>
	public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

	private static readonly IBrush Paper = new SolidColorBrush(Color.Parse("#EFE7D6"));
	private static readonly IBrush Dust = new SolidColorBrush(Color.Parse("#9C95B0"));
	private static readonly IBrush Lamp = new SolidColorBrush(Color.Parse("#F0B25A"));
	private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1D1A2E"));
	private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#9C95B0"), 0.3);

	private const double STROKE = 2;

	static DownloadGlyph() => AffectsRender<DownloadGlyph>(StateProperty, ProgressProperty);

	protected override Size MeasureOverride(Size availableSize) => new(28, 28);

	public override void Render(DrawingContext context)
	{
		var size = Math.Min(Bounds.Width, Bounds.Height);
		var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
		var radius = size / 2 - STROKE;

		switch (State)
		{
			case DownloadState.NotDownloaded:
				context.DrawEllipse(null, new Pen(Dust, STROKE), center, radius, radius);
				var arrow = new Pen(Paper, STROKE, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
				context.DrawLine(arrow, center + new Point(0, -radius * 0.45), center + new Point(0, radius * 0.4));
				context.DrawGeometry(null, arrow, Polyline(center, radius, (-0.35, 0.08), (0, 0.42), (0.35, 0.08)));
				break;

			case DownloadState.Downloading:
				context.DrawEllipse(null, new Pen(Track, STROKE), center, radius, radius);
				context.DrawGeometry(null, new Pen(Lamp, STROKE + 0.5, lineCap: PenLineCap.Round), Arc(center, radius, Math.Clamp(Progress, 0.02, 1)));
				var stop = radius * 0.38;
				context.FillRectangle(Paper, new Rect(center.X - stop, center.Y - stop, stop * 2, stop * 2), 1.5f);
				break;

			case DownloadState.Downloaded:
				context.DrawEllipse(Lamp, null, center, radius + STROKE / 2, radius + STROKE / 2);
				var check = new Pen(Ink, STROKE + 0.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
				context.DrawGeometry(null, check, Polyline(center, radius, (-0.42, 0.02), (-0.12, 0.32), (0.45, -0.3)));
				break;
		}
	}

	/// <summary>A polyline through points given as fractions of the radius from the centre.</summary>
	private static StreamGeometry Polyline(Point center, double radius, params (double X, double Y)[] points)
	{
		var geometry = new StreamGeometry();
		using var ctx = geometry.Open();
		ctx.BeginFigure(center + new Point(points[0].X * radius, points[0].Y * radius), false);
		for (var i = 1; i < points.Length; i++)
			ctx.LineTo(center + new Point(points[i].X * radius, points[i].Y * radius));
		ctx.EndFigure(false);
		return geometry;
	}

	/// <summary>A clockwise arc from twelve o'clock covering <paramref name="fraction"/> of the circle.</summary>
	private static StreamGeometry Arc(Point center, double radius, double fraction)
	{
		var geometry = new StreamGeometry();
		using var ctx = geometry.Open();
		if (fraction >= 0.999)
		{
			ctx.BeginFigure(center + new Point(0, -radius), false);
			ctx.ArcTo(center + new Point(0, radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
			ctx.ArcTo(center + new Point(0, -radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
		}
		else
		{
			var angle = fraction * 2 * Math.PI;
			ctx.BeginFigure(center + new Point(0, -radius), false);
			ctx.ArcTo(center + new Point(radius * Math.Sin(angle), -radius * Math.Cos(angle)), new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
		}
		ctx.EndFigure(false);
		return geometry;
	}
}
