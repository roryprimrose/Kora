using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora;

internal sealed partial class OwnedSkillEditorController
{
    private async Task Rename(OwnedSkillAuthoringCommand command, OwnedSkillAuthoringAuthority authority)
    {
        if (window is not { } opened || lifetime is null || renames is null)
        { throw new InvalidOperationException("Owned package rename is unavailable in this host."); }
        if (dirty)
        { throw new InvalidOperationException("The unsaved draft is retained. Save as a new immutable version or explicitly discard it before renaming a saved package."); }
        var recovery = command.Operation is "recover-rename";
        var pending = opened.PendingRenames.SelectedItem as OwnedSkillRename;
        var selected = opened.Saved.SelectedItem as OwnedDeclarativeSkillSnapshot;
        var id = command.Id ?? (recovery ? pending?.Original.Document.Id : selected?.Document.Id);
        var version = command.Version ?? (recovery ? pending?.Original.Document.Version : selected?.Document.Version);
        if (id is null || version is null)
        { throw new InvalidOperationException("List and select the exact saved package or retained rename first."); }
        var newId = command.Literal ?? opened.RenameId.Text;
        var exact = Interlocked.Read(ref generation);
        bool Current() => Eligible() && authority.IsCurrent() && !dirty && exact == Interlocked.Read(ref generation);
        if (invocation is not null) { await invocation.CloseAndWait(); }
        var saved = await renames.Rename(id, version, newId, recovery, authority, Current, opened, lifetime.Token);
        if (saved is null) { return; }
        if (!Current())
        { throw new OwnedSkillRenameUnconfirmedException(OwnedSkillRenameEffect.NativeReadbackVerified,
            new InvalidOperationException("The editor changed before result adoption; its draft was retained.")); }
        opened.Saved.ItemsSource = await workflow.List(authority, lifetime.Token);
        opened.PendingRenames.ItemsSource = await renames.Pending(authority, lifetime.Token);
        if (saved.Document.ObservationWorkflow is null) { ShowSaved(opened, saved); }
        else { validated = null; source = null; dirty = false; uncertain = false; }
        opened.Report("Complete package rename and old-name absence verified. New identity is inactive; old references were not retargeted. "
            + "List/Reopen the exact new ID/version; use requires fresh independent review. Observation workflows retain their separate editor.");
    }
}
