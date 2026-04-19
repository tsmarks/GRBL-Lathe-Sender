using System;
using System.Windows;
using System.Windows.Media;

namespace GRBL_Lathe_Control;

internal static class ResponsiveLayoutHelper
{
    public static void UpdateScale(FrameworkElement host, ScaleTransform transform, double designWidth, double designHeight)
    {
        if (host.ActualWidth <= 0 || host.ActualHeight <= 0)
        {
            return;
        }

        var widthScale = host.ActualWidth / designWidth;
        var heightScale = host.ActualHeight / designHeight;
        var scale = Math.Min(1.0, Math.Min(widthScale, heightScale));

        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            scale = 1.0;
        }

        transform.ScaleX = scale;
        transform.ScaleY = scale;
    }
}
