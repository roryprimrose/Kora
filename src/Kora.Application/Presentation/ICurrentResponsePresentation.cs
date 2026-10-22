using Kora.Core.Presentation;

namespace Kora.Application.Presentation;

public interface ICurrentResponsePresentation
{
    Func<bool> CaptureLifetime();
    bool Dismiss(Func<bool> admitted);
    bool OpenDetails(AdmittedDetailContent content, Func<bool> admitted);
}
