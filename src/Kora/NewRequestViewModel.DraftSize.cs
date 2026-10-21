using Kora.Application.Presentation;

namespace Kora;

internal sealed partial class NewRequestViewModel
{
    public string DraftSizeFeedback => ComposerDraftSizeFeedback.Format(Composer.DraftMeasurement);
}
