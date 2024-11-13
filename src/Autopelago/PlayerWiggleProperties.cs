using Avalonia;
using Avalonia.Media;

namespace Autopelago;

public sealed class PlayerWiggleProperties : AvaloniaObject
{
    public static readonly AvaloniaProperty<double> WiggleFactorProperty =
        AvaloniaProperty.RegisterAttached<Visual, double>("WiggleFactor", typeof(double), 0);

    public static readonly AvaloniaProperty<double> TargetAngleProperty =
        AvaloniaProperty.RegisterAttached<Visual, double>("TargetAngle", typeof(double), 0);

    static PlayerWiggleProperties()
    {
        WiggleFactorProperty.Changed.AddClassHandler<Visual>(HandleAngleChanged);
        TargetAngleProperty.Changed.AddClassHandler<Visual>(HandleAngleChanged);
    }

    private static void HandleAngleChanged(Visual tgt, AvaloniaPropertyChangedEventArgs args)
    {
        // HACK: this relies way too much on the exact way I've structured the transform on the
        // player image itself... maybe revisit if that becomes an issue.
        ((RotateTransform)((TransformGroup)tgt.RenderTransform!).Children[1]).Angle = GetWiggleFactor(tgt) + GetTargetAngle(tgt);
    }

    public static double GetWiggleFactor(Visual tgt)
    {
        return AvaloniaObjectExtensions.GetValue(tgt, WiggleFactorProperty);
    }

    public static void SetWiggleFactor(Visual tgt, double value)
    {
        tgt.SetValue(WiggleFactorProperty, value);
    }

    public static double GetTargetAngle(Visual tgt)
    {
        return AvaloniaObjectExtensions.GetValue(tgt, TargetAngleProperty);
    }

    public static void SetTargetAngle(Visual tgt, double value)
    {
        tgt.SetValue(TargetAngleProperty, value);
    }
}
