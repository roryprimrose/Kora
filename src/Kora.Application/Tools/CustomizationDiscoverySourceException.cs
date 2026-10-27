namespace Kora.Application.Tools;

/// <summary>Scoped recovery without copying source text, paths or native error messages into presentation or diagnostics.</summary>
public sealed class CustomizationDiscoverySourceException(int selectedEntry, Exception innerException)
    : InvalidOperationException($"Selected metadata entry {selectedEntry} is stale, invalid or unavailable. Choose and review a fresh snapshot.", innerException)
{
    public int SelectedEntry { get; } = selectedEntry;
}
