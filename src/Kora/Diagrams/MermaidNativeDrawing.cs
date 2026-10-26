using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;

namespace Kora.Diagrams;

internal static class MermaidNativeDrawing
{
    internal static Control Create(MermaidDrawing drawing)
    {
        var canvas = new Canvas { Width = drawing.Width, Height = drawing.Height };
        AutomationProperties.SetName(canvas, "Passive native diagram; exact accessible source follows");
        foreach (var line in drawing.Lines)
        {
            AddLine(canvas, line.X1, line.Y1, line.X2, line.Y2, line.Dashed);
            if (line.Arrow)
            {
                var angle = Math.Atan2(line.Y2 - line.Y1, line.X2 - line.X1);
                AddLine(canvas, line.X2, line.Y2, line.X2 - 10 * Math.Cos(angle - 0.5), line.Y2 - 10 * Math.Sin(angle - 0.5), false);
                AddLine(canvas, line.X2, line.Y2, line.X2 - 10 * Math.Cos(angle + 0.5), line.Y2 - 10 * Math.Sin(angle + 0.5), false);
            }
            AddText(canvas, line.Text, Math.Min(line.X1, line.X2) + 4, (line.Y1 + line.Y2) / 2 - 24, 220);
        }
        foreach (var box in drawing.Boxes)
        {
            var border = new Border { Width = box.Width, Height = box.Height, BorderThickness = new Thickness(1) };
            border.Bind(Border.BorderBrushProperty, canvas.GetResourceObservable("KoraForegroundBrush"));
            border.Bind(Border.BackgroundProperty, canvas.GetResourceObservable("KoraWindowBackgroundBrush"));
            Canvas.SetLeft(border, box.X);
            Canvas.SetTop(border, box.Y);
            canvas.Children.Add(border);
            AddText(canvas, box.Text, box.X + 8, box.Y + 8, box.Width - 16);
        }
        return canvas;
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, bool dashed)
    {
        var line = new Line { StartPoint = new(x1, y1), EndPoint = new(x2, y2), StrokeThickness = 1.5,
            StrokeDashArray = dashed ? new Avalonia.Collections.AvaloniaList<double> { 4, 4 } : null };
        line.Bind(Shape.StrokeProperty, canvas.GetResourceObservable("KoraForegroundBrush"));
        canvas.Children.Add(line);
    }

    private static void AddText(Canvas canvas, string value, double x, double y, double width)
    {
        var text = new TextBlock { Text = value, FontSize = 13, Width = width, TextWrapping = TextWrapping.Wrap };
        text.Bind(TextBlock.ForegroundProperty, canvas.GetResourceObservable("KoraForegroundBrush"));
        Canvas.SetLeft(text, x);
        Canvas.SetTop(text, y);
        canvas.Children.Add(text);
    }
}
