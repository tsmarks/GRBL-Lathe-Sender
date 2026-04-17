using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GRBL_Lathe_Control.Models;

namespace GRBL_Lathe_Control.Controls;

public sealed class ToolPathPreviewControl : FrameworkElement
{
    private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(8, 14, 28));
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromArgb(45, 148, 163, 184));
    private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
    private static readonly Brush CutBrush = new SolidColorBrush(Color.FromRgb(244, 114, 53));
    private static readonly Brush RapidBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
    private static readonly Brush MarkerBrush = new SolidColorBrush(Color.FromRgb(250, 204, 21));
    private static readonly Pen GridPen = CreateGridPen();
    private static readonly Pen AxisPen = CreateAxisPen();
    private static readonly Pen CutPen = CreateCutPen();
    private static readonly Pen RapidPen = CreateRapidPen();
    private const double MinZoom = 0.4;
    private const double MaxZoom = 12;
    private const double ZoomStep = 1.15;
    private double _zoom = 1;
    private Vector _panOffset;
    private bool _isPanning;
    private Point _lastPanPosition;
    private Rect _lastPlotBounds;

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments),
        typeof(IEnumerable<ToolPathSegment>),
        typeof(ToolPathPreviewControl),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.AffectsRender,
            static (dependencyObject, _) => ((ToolPathPreviewControl)dependencyObject).ResetView()));

    public static readonly DependencyProperty CurrentXProperty = DependencyProperty.Register(
        nameof(CurrentHorizontal),
        typeof(double),
        typeof(ToolPathPreviewControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentZProperty = DependencyProperty.Register(
        nameof(CurrentVertical),
        typeof(double),
        typeof(ToolPathPreviewControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HorizontalAxisLabelProperty = DependencyProperty.Register(
        nameof(HorizontalAxisLabel),
        typeof(string),
        typeof(ToolPathPreviewControl),
        new FrameworkPropertyMetadata("Z", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VerticalAxisLabelProperty = DependencyProperty.Register(
        nameof(VerticalAxisLabel),
        typeof(string),
        typeof(ToolPathPreviewControl),
        new FrameworkPropertyMetadata("X", FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<ToolPathSegment>? Segments
    {
        get => (IEnumerable<ToolPathSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double CurrentHorizontal
    {
        get => (double)GetValue(CurrentXProperty);
        set => SetValue(CurrentXProperty, value);
    }

    public double CurrentVertical
    {
        get => (double)GetValue(CurrentZProperty);
        set => SetValue(CurrentZProperty, value);
    }

    public string HorizontalAxisLabel
    {
        get => (string)GetValue(HorizontalAxisLabelProperty);
        set => SetValue(HorizontalAxisLabelProperty, value);
    }

    public string VerticalAxisLabel
    {
        get => (string)GetValue(VerticalAxisLabelProperty);
        set => SetValue(VerticalAxisLabelProperty, value);
    }

    public ToolPathPreviewControl()
    {
        Focusable = true;
        ToolTip = "Mouse wheel to zoom. Drag to pan. Double-click to reset the preview.";
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var surfaceBounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(BackgroundBrush, null, surfaceBounds, 18, 18);

        if (ActualWidth < 60 || ActualHeight < 60)
        {
            return;
        }

        var plotBounds = new Rect(28, 28, Math.Max(0, ActualWidth - 56), Math.Max(0, ActualHeight - 56));
        _lastPlotBounds = plotBounds;
        var segments = Segments?.ToList() ?? [];

        DrawLegend(drawingContext, plotBounds);

        if (segments.Count == 0)
        {
            DrawPlaceholder(drawingContext, plotBounds);
            return;
        }

        var points = segments.SelectMany(segment => new[]
        {
            new Point(segment.StartZ, segment.StartX),
            new Point(segment.EndZ, segment.EndX)
        }).ToList();

        points.Add(new Point(CurrentHorizontal, CurrentVertical));

        var minHorizontal = points.Min(point => point.X);
        var maxHorizontal = points.Max(point => point.X);
        var minVertical = points.Min(point => point.Y);
        var maxVertical = points.Max(point => point.Y);

        ExpandFlatBounds(ref minHorizontal, ref maxHorizontal);
        ExpandFlatBounds(ref minVertical, ref maxVertical);

        var horizontalRange = maxHorizontal - minHorizontal;
        var verticalRange = maxVertical - minVertical;
        var scale = Math.Min(plotBounds.Width / horizontalRange, plotBounds.Height / verticalRange) * 0.92;
        var plotCenter = new Point(plotBounds.Left + (plotBounds.Width / 2), plotBounds.Top + (plotBounds.Height / 2));
        var centerHorizontal = (minHorizontal + maxHorizontal) / 2;
        var centerVertical = (minVertical + maxVertical) / 2;

        Point Map(double horizontal, double vertical) => new(
            plotCenter.X + (((horizontal - centerHorizontal) * scale * _zoom) + _panOffset.X),
            plotCenter.Y - (((vertical - centerVertical) * scale * _zoom) - _panOffset.Y));

        DrawGrid(drawingContext, plotBounds);
        drawingContext.PushClip(new RectangleGeometry(plotBounds, 18, 18));

        if (minHorizontal <= 0 && maxHorizontal >= 0)
        {
            var axisX = Map(0, centerVertical).X;
            drawingContext.DrawLine(AxisPen, new Point(axisX, plotBounds.Top), new Point(axisX, plotBounds.Bottom));
        }

        if (minVertical <= 0 && maxVertical >= 0)
        {
            var axisY = Map(centerHorizontal, 0).Y;
            drawingContext.DrawLine(AxisPen, new Point(plotBounds.Left, axisY), new Point(plotBounds.Right, axisY));
        }

        foreach (var segment in segments)
        {
            drawingContext.DrawLine(
                segment.IsRapid ? RapidPen : CutPen,
                Map(segment.StartZ, segment.StartX),
                Map(segment.EndZ, segment.EndX));
        }

        var toolPoint = Map(CurrentHorizontal, CurrentVertical);
        drawingContext.DrawEllipse(MarkerBrush, null, toolPoint, 4.5, 4.5);
        drawingContext.Pop();

        DrawAxisLabels(drawingContext, plotBounds);
        DrawInteractionHint(drawingContext, plotBounds);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonDown(eventArgs);

        Focus();
        if (eventArgs.ClickCount == 2)
        {
            ResetView();
            eventArgs.Handled = true;
            return;
        }

        _isPanning = true;
        _lastPanPosition = eventArgs.GetPosition(this);
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        eventArgs.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);

        if (!_isPanning)
        {
            return;
        }

        var currentPosition = eventArgs.GetPosition(this);
        _panOffset += currentPosition - _lastPanPosition;
        _lastPanPosition = currentPosition;
        InvalidateVisual();
        eventArgs.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs eventArgs)
    {
        base.OnMouseLeftButtonUp(eventArgs);
        EndPan();
        eventArgs.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs eventArgs)
    {
        base.OnLostMouseCapture(eventArgs);
        EndPan();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);

        if (_lastPlotBounds.Width <= 0 || _lastPlotBounds.Height <= 0)
        {
            return;
        }

        var zoomFactor = eventArgs.Delta > 0 ? ZoomStep : 1 / ZoomStep;
        var newZoom = Math.Clamp(_zoom * zoomFactor, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - _zoom) < 0.0001)
        {
            return;
        }

        var anchor = _lastPlotBounds.Contains(eventArgs.GetPosition(this))
            ? eventArgs.GetPosition(this)
            : new Point(_lastPlotBounds.Left + (_lastPlotBounds.Width / 2), _lastPlotBounds.Top + (_lastPlotBounds.Height / 2));

        UpdateZoom(anchor, newZoom);
        eventArgs.Handled = true;
    }

    private static void DrawGrid(DrawingContext drawingContext, Rect plotBounds)
    {
        const int divisions = 10;

        for (var index = 0; index <= divisions; index++)
        {
            var x = plotBounds.Left + ((plotBounds.Width / divisions) * index);
            var y = plotBounds.Top + ((plotBounds.Height / divisions) * index);

            drawingContext.DrawLine(GridPen, new Point(x, plotBounds.Top), new Point(x, plotBounds.Bottom));
            drawingContext.DrawLine(GridPen, new Point(plotBounds.Left, y), new Point(plotBounds.Right, y));
        }
    }

    private void DrawPlaceholder(DrawingContext drawingContext, Rect plotBounds)
    {
        DrawGrid(drawingContext, plotBounds);

        var message = CreateText($"Load a G-code file to preview the {HorizontalAxisLabel}/{VerticalAxisLabel} toolpath.", 16);
        var subMessage = CreateText("Rapid moves are blue. Cutting moves are orange.", 13);

        var messageOrigin = new Point(
            plotBounds.Left + ((plotBounds.Width - message.Width) / 2),
            plotBounds.Top + ((plotBounds.Height - message.Height) / 2) - 12);

        var subMessageOrigin = new Point(
            plotBounds.Left + ((plotBounds.Width - subMessage.Width) / 2),
            messageOrigin.Y + message.Height + 10);

        drawingContext.DrawText(message, messageOrigin);
        drawingContext.DrawText(subMessage, subMessageOrigin);
        DrawAxisLabels(drawingContext, plotBounds);
    }

    private void DrawLegend(DrawingContext drawingContext, Rect plotBounds)
    {
        var cutStart = new Point(plotBounds.Left, plotBounds.Top - 10);
        var rapidStart = new Point(plotBounds.Left + 120, plotBounds.Top - 10);

        drawingContext.DrawLine(CutPen, cutStart, new Point(cutStart.X + 20, cutStart.Y));
        drawingContext.DrawText(CreateText("Cut", 12), new Point(cutStart.X + 28, cutStart.Y - 9));

        drawingContext.DrawLine(RapidPen, rapidStart, new Point(rapidStart.X + 20, rapidStart.Y));
        drawingContext.DrawText(CreateText("Rapid", 12), new Point(rapidStart.X + 28, rapidStart.Y - 9));
    }

    private void DrawAxisLabels(DrawingContext drawingContext, Rect plotBounds)
    {
        drawingContext.DrawText(CreateText(HorizontalAxisLabel, 13), new Point(plotBounds.Right - 16, plotBounds.Bottom + 4));
        drawingContext.DrawText(CreateText(VerticalAxisLabel, 13), new Point(plotBounds.Left - 16, plotBounds.Top - 4));
    }

    private void DrawInteractionHint(DrawingContext drawingContext, Rect plotBounds)
    {
        var hintText = CreateText($"Wheel zoom | Drag pan | Double-click reset | {Math.Round(_zoom * 100):0}%", 11);
        drawingContext.DrawText(
            hintText,
            new Point(plotBounds.Left, plotBounds.Bottom + 8));
    }

    private void UpdateZoom(Point anchor, double newZoom)
    {
        var plotCenter = new Point(_lastPlotBounds.Left + (_lastPlotBounds.Width / 2), _lastPlotBounds.Top + (_lastPlotBounds.Height / 2));
        var unzoomedAnchor = plotCenter + ((anchor - plotCenter - _panOffset) / _zoom);
        _zoom = newZoom;
        _panOffset = anchor - (unzoomedAnchor - plotCenter) * _zoom - plotCenter;
        InvalidateVisual();
    }

    private void EndPan()
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = null;
    }

    private void ResetView()
    {
        _zoom = 1;
        _panOffset = default;
        EndPan();
        InvalidateVisual();
    }

    private static void ExpandFlatBounds(ref double minimum, ref double maximum)
    {
        if (Math.Abs(maximum - minimum) >= 0.001)
        {
            return;
        }

        minimum -= 5;
        maximum += 5;
    }

    private FormattedText CreateText(string value, double size)
    {
        return new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size,
            TextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    private static Pen CreateGridPen()
    {
        var pen = new Pen(GridBrush, 1);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateAxisPen()
    {
        var pen = new Pen(AxisBrush, 1.2);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateCutPen()
    {
        var pen = new Pen(CutBrush, 1.8);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateRapidPen()
    {
        var pen = new Pen(RapidBrush, 1.3)
        {
            DashStyle = DashStyles.Dash
        };
        pen.Freeze();
        return pen;
    }
}
