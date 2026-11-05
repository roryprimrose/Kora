using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.VisualTree;

using Kora.Application.Voice;

namespace Kora;

public sealed class SpeechCaptionEmphasisLayer : Control
{
    private TextBox? reader;
    private SpeechCaptionEmphasis? range;

    public SpeechCaptionEmphasisLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
    }

    internal void Apply(TextBox target, SpeechCaptionEmphasis? emphasis)
    {
        if (emphasis is { } span && (span.Start < 0 || span.Length <= 0
            || span.Start > (target.Text?.Length ?? 0) - span.Length))
        {
            throw new InvalidOperationException("Caption emphasis is outside its admitted source slice.");
        }
        reader = target;
        range = emphasis;
        InvalidateVisual();
    }

    internal IReadOnlyList<Rect> Underlines()
    {
        if (range is not { } span || reader?.GetVisualDescendants().OfType<TextPresenter>().SingleOrDefault() is not { } presenter
            || presenter.TranslatePoint(default, this) is not { } origin)
        {
            return [];
        }
        return presenter.TextLayout.HitTestTextRange(span.Start, span.Length)
            .Select(rect => new Rect(rect.X + origin.X, rect.Bottom + origin.Y - 2, rect.Width, 2)
                .Intersect(new Rect(Bounds.Size)))
            .Where(rect => rect.Width > 0 && rect.Height > 0).ToArray();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        foreach (var underline in Underlines()) { context.DrawRectangle(reader!.Foreground, null, underline); }
    }
}
