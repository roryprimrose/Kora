namespace Kora.Core.Presentation;

/// <summary>The resource and presentation boundary for one host-owned presence surface.</summary>
public interface IPresenceRenderTarget : IDisposable
{
    /// <summary>Draws on the rendering worker; the target must not retain the particle span.</summary>
    void Draw(ReadOnlySpan<PresenceParticle> particles, double width, double height, double scaling,
        byte red, byte green, byte blue, double opacity);

    /// <summary>Presents the completed buffer on the UI thread, only after host eligibility is rechecked.</summary>
    void Present();

    /// <summary>Hides immediately on the UI thread without waiting for the drawing worker.</summary>
    void Hide();

    /// <summary>Releases drawing resources on their owning worker before that worker exits.</summary>
    void ReleaseDrawingResources();
}