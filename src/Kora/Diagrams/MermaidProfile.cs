using System.Text;

namespace Kora.Diagrams;

internal static class MermaidProfile
{
    internal const string Name = "native-mermaid-v1";
    internal const int MaximumDiagramBytes = 32 * 1024;
    internal const int MaximumNodes = 500;
    internal const int MaximumEdges = 1000;
    internal const int MaximumDiagrams = 8;
    internal const int MaximumSvgBytes = 1024 * 1024;
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);
    internal static readonly UTF8Encoding Utf8 = new(false, true);
}
