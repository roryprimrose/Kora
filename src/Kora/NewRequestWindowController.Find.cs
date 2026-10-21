using Kora.Application.Hosting;

namespace Kora;

internal sealed partial class NewRequestWindowController
{
    private void BindDraftFind(NewRequestWindow opened, NewRequestComposer state)
    {
        opened.BindDraftFind(() =>
        {
            var revision = access.ControlRevision;
            bool Current() => !disposed && ReferenceEquals(window, opened) && ReferenceEquals(composer, state)
                && access.CanInspect && access.CanControl && access.ControlRevision == revision
                && main.CanRevealPrivatePresentation && state.CanEdit && !state.Busy;
            return Current() ? Current : null;
        }, main);
    }
}
