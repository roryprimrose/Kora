using Avalonia.Controls;

using Kora.Core.Presentation;
using Kora.Diagrams;

namespace Kora;

public sealed partial class DetailContentView
{
    private MermaidDetailView? mermaid;

    private void AttachMermaid(AdmittedDetailContent content, IMermaidRenderer? renderer)
    {
        var sources = MermaidFences.Read(content);
        if (sources.Count == 0) { return; }
        var generation = state.Generation;
        mermaid = new(sources, () => !cleared && state.Generation == generation
            && state.Content is { } current && current.IsSameSnapshot(content), renderer ?? new MermaidRenderer());
        Required<ContentControl>("MermaidContent").Content = mermaid;
    }

    private void RetireMermaid()
    {
        mermaid?.Retire();
        Required<ContentControl>("MermaidContent").Content = null;
    }

    internal Task MermaidQuiescence => mermaid?.WhenQuiescent ?? Task.CompletedTask;
}
