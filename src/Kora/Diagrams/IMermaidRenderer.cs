namespace Kora.Diagrams;

internal interface IMermaidRenderer
{
    Task<MermaidResult> RenderAsync(string source, CancellationToken token);
}
