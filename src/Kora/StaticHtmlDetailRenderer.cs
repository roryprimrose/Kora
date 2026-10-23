using System.Diagnostics;

using Kora.Core.Presentation;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class StaticHtmlDetailRenderer(
    ILogger<StaticHtmlDetailRenderer> logger, bool qualified = false, string profile = StaticHtmlProfile.Name)
{
    private static readonly ActivitySource ActivitySource =
        new("Kora.Desktop", typeof(StaticHtmlDetailRenderer).Assembly.GetName().Version!.ToString());

    internal NativeDocumentResult Render(AdmittedDetailContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var activity = ActivitySource.StartActivity("presentation.static-html.render");
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.SetTag("kora.item.id", content.Reference.ItemId.Value);
        activity?.SetTag("kora.item.revision", content.Reference.Revision);
        activity?.SetTag("kora.presentation.profile", StaticHtmlProfile.Name);
        if (content.SessionSource is { } session)
        {
            activity?.SetTag("kora.session.id", session.SessionId.Value);
            activity?.SetTag("kora.request.id", session.RequestId.Value);
            activity?.SetTag("kora.task.id", session.TaskId.Value);
        }
        else if (content.HistorySession is { } historySession)
        {
            activity?.SetTag("kora.session.id", historySession.Value);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Kind != DetailContentKind.StaticHtml)
        {
            throw new InvalidDataException("Only explicitly admitted static HTML can enter this renderer.");
        }
        if (!string.Equals(profile, StaticHtmlProfile.Name, StringComparison.Ordinal))
        {
            return Fallback(content, "UnknownProfile",
                "Static HTML profile unknown; exact whole-source fallback. Nothing was rendered or truncated.");
        }
        if (!qualified)
        {
            return Fallback(content, "Unqualified",
                "static-html-v1 qualification unavailable; exact whole-source fallback. Static rendering is not enabled.");
        }
        var document = StaticHtmlParser.Parse(content.Source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.IsFallback)
        {
            return Fallback(content, document.Failure.ToString(),
                $"static-html-v1 rejected ({document.Failure}); exact whole-source fallback. Nothing was stripped or truncated.",
                document);
        }
        activity?.SetStatus(ActivityStatusCode.Ok);
        return new(content.Source, document.SemanticText,
            "Rendered static-html-v1 strict inert fragment. No attributes, resources, active controls or document styling. Source remains exact.",
            false, [], document.BlockCount, document.NodeCount, document.Depth, document);
    }

    private NativeDocumentResult Fallback(AdmittedDetailContent content, string reason, string status,
        StaticHtmlDocument? document = null)
    {
        RenderFallback(logger, reason, content.Reference.ItemId.Value, content.Reference.Revision, StaticHtmlProfile.Name);
        return new(content.Source, content.Source, status, true, [],
            document?.BlockCount ?? 0, document?.NodeCount ?? 0, document?.Depth ?? 0, document);
    }
}
