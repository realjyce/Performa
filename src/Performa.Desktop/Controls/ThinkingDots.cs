using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Performa.Desktop.Controls;

/// <summary>
/// The assistant's "working on it" mark. Three dots run one continuous loop:
/// a staggered wave, then they gather into a rotating prism, then they collapse
/// into a single filled circle that breathes, then they split back to the wave.
///
/// Drawn rather than composed from XAML animations because every stage has to
/// hand its exact geometry to the next one. Keyframes on separate elements drift
/// apart at the seams; one clock driving one Render cannot.
/// </summary>
public sealed class ThinkingDots : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<ThinkingDots, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<ThinkingDots, IBrush?>(nameof(DotBrush));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public IBrush? DotBrush
    {
        get => GetValue(DotBrushProperty);
        set => SetValue(DotBrushProperty, value);
    }

    // Stage lengths in seconds. They sum to the loop, and each stage both starts
    // and ends on the pose its neighbour expects, so the loop has no seam.
    private const double Wave = 1.8;
    private const double Gather = 0.55;
    private const double Spin = 1.7;
    private const double Merge = 0.5;
    private const double Breathe = 1.3;
    private const double Split = 0.55;
    private const double Loop = Wave + Gather + Spin + Merge + Breathe + Split;

    private const double HopPeriod = 0.6;   // one dot's bounce
    private const double HopStagger = 0.6;  // radians of lag between dots

    private readonly Stopwatch _clock = new();
    private readonly DispatcherTimer _timer;

    public ThinkingDots()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _timer.Tick += (_, _) => InvalidateVisual();
    }

    static ThinkingDots()
    {
        AffectsRender<ThinkingDots>(DotBrushProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty) Sync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _timer.Stop();
        _clock.Stop();
    }

    /// <summary>No clock runs while the mark is hidden, so an idle page costs nothing.</summary>
    private void Sync()
    {
        if (IsActive && _attached)
        {
            if (!_clock.IsRunning) { _clock.Restart(); _timer.Start(); }
        }
        else
        {
            _timer.Stop();
            _clock.Reset();
        }
        InvalidateVisual();
    }

    private bool _attached;

    private static double Ease(double t) => t * t * (3 - 2 * t);

    private static Point Lerp(Point a, Point b, double t)
        => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public override void Render(DrawingContext ctx)
    {
        if (!IsActive) return;

        var brush = DotBrush ?? Brushes.White;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);

        var dotR = unit * 0.11;          // a single dot at rest
        var gap = dotR * 3.0;            // row spacing
        var ring = dotR * 2.3;           // prism circumradius
        var bigR = dotR * 2.1;           // the merged circle
        var hop = dotR * 1.4;            // wave height

        var t = _clock.Elapsed.TotalSeconds % Loop;

        Point Row(int i) => new(cx + (i - 1) * gap, cy);

        Point Prism(int i, double rot)
        {
            var a = -Math.PI / 2 + i * (2 * Math.PI / 3) + rot;
            return new Point(cx + ring * Math.Cos(a), cy + ring * Math.Sin(a));
        }

        for (var i = 0; i < 3; i++)
        {
            Point p;
            double r;

            if (t < Wave)
            {
                // Only the upward half of the sine is used, so the dots hop off a
                // line rather than oscillating through it, and all three are back
                // at rest exactly when the stage ends.
                var phase = t / HopPeriod * 2 * Math.PI - i * HopStagger;
                var lift = Math.Max(0, Math.Sin(phase));
                p = new Point(Row(i).X, cy - hop * lift);
                r = dotR;
            }
            else if (t < Wave + Gather)
            {
                var k = Ease((t - Wave) / Gather);
                p = Lerp(Row(i), Prism(i, 0), k);
                r = dotR;
            }
            else if (t < Wave + Gather + Spin)
            {
                // A whole turn, so the prism lands back where the merge expects it.
                var k = (t - Wave - Gather) / Spin;
                p = Prism(i, Ease(k) * 2 * Math.PI);
                r = dotR;
            }
            else if (t < Wave + Gather + Spin + Merge)
            {
                var k = (t - Wave - Gather - Spin) / Merge;
                p = Lerp(Prism(i, 0), new Point(cx, cy), Ease(k));
                // Three opaque circles landing on one point read as a single
                // circle, which is what lets the collapse look like a merge.
                // Growth is held back until they have nearly met, otherwise the
                // half-merged frames read as one lumpy blob instead of dots.
                var grow = Ease(Math.Clamp((k - 0.45) / 0.55, 0, 1));
                r = dotR + (bigR - dotR) * grow;
            }
            else if (t < Wave + Gather + Spin + Merge + Breathe)
            {
                var k = (t - Wave - Gather - Spin - Merge) / Breathe;
                p = new Point(cx, cy);
                r = bigR * (1 + 0.24 * Math.Sin(k * 2 * Math.PI * 2));
            }
            else
            {
                var k = (t - Wave - Gather - Spin - Merge - Breathe) / Split;
                p = Lerp(new Point(cx, cy), Row(i), Ease(k));
                // Mirror of the merge: shrink first, separate second, so the
                // circle never pulls apart while it is still wide.
                var shrink = Ease(Math.Clamp(k / 0.55, 0, 1));
                r = bigR + (dotR - bigR) * shrink;
            }

            ctx.DrawEllipse(brush, null, p, r, r);
        }
    }
}
