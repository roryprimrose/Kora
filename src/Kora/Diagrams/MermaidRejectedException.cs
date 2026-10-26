namespace Kora.Diagrams;

internal sealed class MermaidRejectedException(MermaidFailure failure) : Exception
{
    internal MermaidFailure Failure { get; } = failure;
}
