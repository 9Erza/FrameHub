using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FrameHub.Core.Models.Benchmarking;
using FrameHub.Core.Services.Benchmarking;

namespace FrameHub.App.Controls;

using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

public sealed class FrametimeChart : FrameworkElement
{
    private BenchmarkChartPoint? _hoverPoint;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<BenchmarkChartPoint>), typeof(FrametimeChart),
        new FrameworkPropertyMetadata(Array.Empty<BenchmarkChartPoint>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<BenchmarkChartPoint> Points
    {
        get => (IReadOnlyList<BenchmarkChartPoint>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public FrametimeChart()
    {
        MinHeight = 220;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) =>
        {
            _hoverPoint = null;
            ToolTip = null;
            InvalidateVisual();
        };
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        WpfBrush background = Resource("BrushSurface", new SolidColorBrush(WpfColor.FromRgb(19, 22, 27)));
        WpfBrush border = Resource("BrushBorderSubtle", new SolidColorBrush(WpfColor.FromRgb(36, 42, 50)));
        WpfBrush muted = Resource("BrushTextMuted", WpfBrushes.Gray);
        WpfBrush line = Resource("BrushAccentPrimary", WpfBrushes.DeepSkyBlue);
        drawingContext.DrawRoundedRectangle(background, new WpfPen(border, 1), new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1)), 8, 8);
        if (Points.Count == 0 || ActualWidth < 80 || ActualHeight < 80) return;

        Rect plot = new(52, 14, Math.Max(1, ActualWidth - 68), Math.Max(1, ActualHeight - 44));
        double maxX = Points[^1].ElapsedSeconds;
        double maxY = Math.Max(20, Points.Max(point => point.FrameTimeMs) * 1.05);
        foreach ((double value, string _) in new[] { (4.17, "240"), (8.33, "120"), (16.67, "60") })
        {
            if (value > maxY) continue;
            double y = plot.Bottom - value / maxY * plot.Height;
            var gridBrush = border.Clone();
            gridBrush.Opacity = 0.72;
            drawingContext.DrawLine(new WpfPen(gridBrush, 1) { DashStyle = DashStyles.Dash }, new WpfPoint(plot.Left, y), new WpfPoint(plot.Right, y));
            DrawText(drawingContext, $"{value:0.00}", muted, new WpfPoint(8, y - 7));
        }

        IReadOnlyList<BenchmarkChartPoint> rendered = BenchmarkChartData.DownsampleMinMax(Points, Math.Max(1, (int)plot.Width));
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            for (int index = 0; index < rendered.Count; index++)
            {
                WpfPoint point = Map(rendered[index], plot, maxX, maxY);
                if (index == 0) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
            }
        }

        geometry.Freeze();
        drawingContext.PushClip(new RectangleGeometry(plot));
        drawingContext.DrawGeometry(null, new WpfPen(line, 1.5), geometry);
        if (_hoverPoint is BenchmarkChartPoint hoverPoint)
        {
            WpfPoint marker = Map(hoverPoint, plot, maxX, maxY);
            drawingContext.DrawEllipse(background, new WpfPen(line, 2), marker, 4.5, 4.5);
        }
        drawingContext.Pop();
        DrawText(drawingContext, "0 s", muted, new WpfPoint(plot.Left, plot.Bottom + 5));
        DrawText(drawingContext, $"{maxX:0.0} s", muted, new WpfPoint(plot.Right - 34, plot.Bottom + 5));
    }

    private void OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (Points.Count == 0 || ActualWidth < 80) return;
        double x = Math.Clamp((e.GetPosition(this).X - 52) / Math.Max(1, ActualWidth - 68), 0, 1) * Points[^1].ElapsedSeconds;
        BenchmarkChartPoint nearest = Points.MinBy(point => Math.Abs(point.ElapsedSeconds - x));
        _hoverPoint = nearest;
        ToolTip = string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:0.000} s  ·  {1:0.00} ms", nearest.ElapsedSeconds, nearest.FrameTimeMs);
        InvalidateVisual();
    }

    private static WpfPoint Map(BenchmarkChartPoint point, Rect plot, double maxX, double maxY) => new(
        plot.Left + point.ElapsedSeconds / Math.Max(maxX, double.Epsilon) * plot.Width,
        plot.Bottom - Math.Min(point.FrameTimeMs, maxY) / maxY * plot.Height);

    private WpfBrush Resource(string key, WpfBrush fallback) => TryFindResource(key) as WpfBrush ?? fallback;

    private void DrawText(DrawingContext context, string text, WpfBrush brush, WpfPoint point)
    {
        System.Windows.Media.FontFamily fontFamily = TryFindResource("FrameHubDisplayFontFamily") as System.Windows.Media.FontFamily ?? new System.Windows.Media.FontFamily("Segoe UI");
        context.DrawText(
            new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, new Typeface(fontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 10.5, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip),
            point);
    }
}
