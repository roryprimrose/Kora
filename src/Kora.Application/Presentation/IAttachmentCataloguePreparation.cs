namespace Kora.Application.Presentation;

/// <summary>Preparation of the singleton native metadata catalogue, without executing its query.</summary>
public interface IAttachmentCataloguePreparation
{
    /// <summary>Captures a non-reviving presenter/window lifetime before dispatch.</summary>
    Func<bool> CaptureLifetime();

    /// <summary>Opens/focuses the existing catalogue and prepares only ExactId and its query text.</summary>
    /// <remarks>Throws unless the native view acknowledges focus and preparation under original admission.</remarks>
    void Prepare(Guid sessionId, Func<bool> admitted);
}
