using System.Text;

using Kora.Core.Customizations;

namespace Kora;

internal sealed partial class CustomizationWindowController
{
    private async Task AdaptSkill(Func<bool> current, CancellationToken token)
    {
        if (source is not { Kind: CustomizationKind.Skill, Scope: CustomizationScope.User } || baseline is null)
        { throw new InvalidOperationException("Inspect one read-consented shared declarative skill first. Current bundled packages require protected executable implementations and cannot be faithfully adapted; nothing is copied."); }
        if (Dirty())
        { throw new InvalidOperationException("Reload the inspected shared source before adapting it. This operation never saves or copies edits to the original."); }
        var original = baseline;
        var name = window!.NewName.Text?.Trim() ?? string.Empty;
        var revision = draftRevision;
        var observed = catalogue.SourceCandidates;
        var failure = catalogue.ReloadFailureRevision;
        bool Exact() => current() && revision == draftRevision && ReferenceEquals(baseline, original)
            && ReferenceEquals(catalogue.SourceCandidates, observed) && catalogue.ReloadFailureRevision == failure;
        var proposal = await management.PrepareSkillAdaptation(original, name, Exact, token);
        RequireCurrent(Exact, token);
        skillAdaptation = proposal;
        adaptationCatalogue = observed;
        adaptationReloadFailure = failure;
        adaptationUnconfirmed = false;
        source = proposal.Destination;
        baseline = null;
        draft = [proposal.Proposed];
        selectedFile = null;
        draftRevision++;
        ShowDraft();
        window.Status.Text = "Kora adaptation draft only. Original untouched; attribution is fixed. Optionally edit plain guidance, then Review and save for complete source/proposal review and separate disabled publication confirmation. No model or runtime authority is used.";
    }

    private async Task<bool> SaveAdaptation(Func<bool> current, CancellationToken token)
    {
        var proposal = skillAdaptation!;
        if (adaptationUnconfirmed)
        { throw new InvalidOperationException("Adaptation publication is unconfirmed. Do not retry. Explicitly inspect the exact destination to reconcile any saved disabled file, or re-inspect the original before preparing a new adaptation."); }
        FlushBuffer();
        var revision = draftRevision;
        var files = draft.ToArray();
        bool Exact() => current() && revision == draftRevision && ReferenceEquals(skillAdaptation, proposal)
            && source == proposal.Destination && ReferenceEquals(catalogue.SourceCandidates, adaptationCatalogue)
            && catalogue.ReloadFailureRevision == adaptationReloadFailure;
        var review = new StringBuilder("CREATE A NEW DISABLED KORA SKILL\nOriginal remains untouched. No model disclosure, tools, grants, dependency authority or activation are inherited.\n"
            + "\nORIGINAL SOURCE\n" + proposal.Original.Source.Location
            + "\n\nHOST-CHOSEN OWNED DESTINATION\n" + proposal.Destination.Location);
        foreach (var file in proposal.Original.Files)
        { review.Append("\n\nCOMPLETE ORIGINAL ").Append(file.Name).Append('\n').Append(file.Text); }
        foreach (var file in files)
        { review.Append("\n\nCOMPLETE PROPOSED ").Append(file.Name).Append('\n').Append(file.Text); }
        try
        {
            var saved = await management.SaveSkillAdaptation(proposal, files,
                async admitted => await window!.Review("Review complete skill adaptation", review.ToString(), admitted,
                    "Continue to separate save confirmation", "Cancel") is "Continue to separate save confirmation",
                async admitted => await window!.Review("Confirm disabled skill adaptation", review.ToString()
                    + "\n\nSave this exact one-file adaptation as NEW and DISABLED? Existing destinations are never overwritten.",
                    admitted, "Save new disabled skill", "Cancel") is "Save new disabled skill",
                Exact, TimeProvider.System, token);
            if (saved is null) { return false; }
            RequireCurrent(Exact, token);
            skillAdaptation = null;
            adaptationCatalogue = null;
            baseline = saved;
            draft = saved.Files.ToList();
            var reloaded = reload.ReloadNow();
            await Refresh(current, token);
            RequireCurrent(current, token);
            // Reopen the exact published bytes, never a newer same-name revision.
            await management.RequireUnchangedSource(saved.Source, saved, current, token);
            var reopened = await management.Read(saved.Source, false, current, token);
            if (!string.Equals(reopened.Revision, saved.Revision, StringComparison.Ordinal))
            { throw new InvalidOperationException("The saved adaptation changed before reopen. Inspect it explicitly; no current success is claimed."); }
            baseline = reopened;
            draft = reopened.Files.ToList();
            ShowDraft();
            window!.Status.Text = reloaded
                ? "New adaptation saved, durably read back and reopened DISABLED with attribution. Original untouched. No model, tool, grant or use authority inherited."
                : "Adaptation saved and reopened DISABLED, but catalogue reload failed. Resolve the source error before separate enablement.";
            return true;
        }
        catch
        {
            adaptationUnconfirmed = true;
            throw;
        }
    }
}
