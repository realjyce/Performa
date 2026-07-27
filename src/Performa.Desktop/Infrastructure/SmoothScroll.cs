using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Performa.Desktop.Infrastructure;

/// <summary>
/// Wheel scrolling with glide. Avalonia's ScrollViewer jumps a whole step per
/// notch; this intercepts the wheel and eases the offset toward a target
/// instead. The easing is exponential against real elapsed time, never a
/// per-frame constant: a per-frame lerp runs at different speeds on different
/// refresh rates, which is exactly the bug the portfolio deck had.
///
/// Applied app-wide from a style. Touchpads with precision deltas feed the
/// same path and stay smooth because the target just moves more often.
/// </summary>
public static class SmoothScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(SmoothScroll));

    public static void SetIsEnabled(ScrollViewer element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(ScrollViewer element) =>
        element.GetValue(IsEnabledProperty);

    private const double StepPerNotch = 110;   // px of travel per wheel notch
    private const double Stiffness = 14;       // higher = snappier settle

    static SmoothScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) =>
        {
            if (args.NewValue is true)
                viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel,
                    Avalonia.Interactivity.RoutingStrategies.Tunnel);
            else
                viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        });
    }

    private sealed class Glide
    {
        public double TargetY;
        public bool Running;
        public long LastTicks;
    }

    private static readonly AttachedProperty<Glide?> GlideProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, Glide?>("Glide", typeof(SmoothScroll));

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;
        // Only vertical wheel motion is smoothed; anything else keeps stock
        // behaviour (shift-scroll, horizontal wheels, zoom modifiers).
        if (e.KeyModifiers != KeyModifiers.None || e.Delta.Y == 0) return;
        var maxY = viewer.Extent.Height - viewer.Viewport.Height;
        if (maxY <= 0) return;

        var glide = viewer.GetValue(GlideProperty);
        if (glide is null)
        {
            glide = new Glide();
            viewer.SetValue(GlideProperty, glide);
        }

        // A fresh gesture starts from wherever the view actually is, so a
        // stale target from a finished glide can't yank the view backward.
        if (!glide.Running) glide.TargetY = viewer.Offset.Y;

        glide.TargetY = Math.Clamp(glide.TargetY - e.Delta.Y * StepPerNotch, 0, maxY);
        e.Handled = true;

        if (glide.Running) return;
        glide.Running = true;
        glide.LastTicks = DateTime.Now.Ticks;

        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        timer.Tick += (_, _) =>
        {
            var now = DateTime.Now.Ticks;
            var dt = Math.Min((now - glide.LastTicks) / (double)TimeSpan.TicksPerSecond, 0.05);
            glide.LastTicks = now;

            var y = viewer.Offset.Y;
            var next = y + (glide.TargetY - y) * (1 - Math.Exp(-Stiffness * dt));

            if (Math.Abs(glide.TargetY - next) < 0.5)
            {
                next = glide.TargetY;
                glide.Running = false;
                timer.Stop();
            }
            viewer.Offset = viewer.Offset.WithY(next);
        };
        timer.Start();
    }
}
