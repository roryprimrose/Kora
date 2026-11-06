using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed partial class OwnedInstructionEditorActions
{
    public ArtifactFileDocument PrepareRestore(OwnedInstructionRestorePoint point)
    {
        var previous = point.Previous;
        var current = point.Current;
        previous.Document.RequireEditableInstruction();
        current.Document.RequireEditableInstruction();
        if (Kind != ArtifactKind.Instruction || previous.Document.Definition.Kind != Kind
            || current.Document.Definition.Kind != Kind
            || !string.Equals(previous.Name, current.Name, StringComparison.Ordinal)
            || !string.Equals(previous.DirectoryIdentity, current.DirectoryIdentity, StringComparison.Ordinal)
            || !string.Equals(previous.CanonicalFileName, current.CanonicalFileName, StringComparison.Ordinal)
            || !string.Equals(current.CanonicalFileName, current.Name + OwnedDefinitionKind.Extension(Kind), StringComparison.Ordinal)
            || !string.Equals(previous.Document.Metadata.GetValueOrDefault("kora-format"), "1", StringComparison.Ordinal)
            || !string.Equals(current.Document.Metadata.GetValueOrDefault("kora-format"), "1", StringComparison.Ordinal)
            || string.Equals(previous.Document.Definition.Digest, current.Document.Definition.Digest, StringComparison.Ordinal))
        { throw new InvalidDataException("Restore requires different complete supported native Instruction bytes from one confirmed same-source Save in this editor."); }
        _ = OwnedInstructionRevision.Validate(previous.Revision);
        _ = OwnedInstructionRevision.Validate(current.Revision);
        _ = Validate(current.Name, current.Document.Text, current.Document.HasBom);
        return Validate(previous.Name, previous.Document.Text, previous.Document.HasBom);
    }

    public Task<InstructionWriteResult> Restore(OwnedInstructionRestorePoint point,
        Func<ArtifactFileDocument, CancellationToken, Task<bool>> exactPreview,
        IInstructionMutationReview review, Func<bool> eligible, CancellationToken token)
    {
        var proposed = PrepareRestore(point);
        var failureRevision = catalogue.ReloadFailureRevision;
        var sourceRetired = 0;
        void Observe(object? sender, EventArgs args)
        {
            if (!IsRefinementSourceObserved(point.Current)) { Interlocked.Exchange(ref sourceRetired, 1); }
        }
        bool Current() => eligible() && Volatile.Read(ref sourceRetired) == 0 && failureRevision == catalogue.ReloadFailureRevision
            && IsRefinementSourceObserved(point.Current);
        async Task RequireCurrent(CancellationToken admitted)
        {
            var live = await store.Read(point.Current.Name, admitted).ConfigureAwait(false);
            admitted.ThrowIfCancellationRequested();
            if (!Current() || live is null
                || !string.Equals(live.Revision, point.Current.Revision, StringComparison.Ordinal))
            { throw new InvalidOperationException("The current restore source changed or became unavailable. No historical overwrite is approved; explicitly inspect/reconcile."); }
            _ = PrepareRestore(point);
        }
        async Task<InstructionWriteResult> Run()
        {
            catalogue.Changed += Observe;
            try
            {
                var result = await Save(point.Current.Name, point.Current, proposed, async admitted =>
                {
                    await RequireCurrent(admitted).ConfigureAwait(false);
                    if (!await exactPreview(proposed, admitted).ConfigureAwait(false)) { return false; }
                    await RequireCurrent(admitted).ConfigureAwait(false);
                    if (!await review.Confirm(point.Current.Name, point.Current, proposed, admitted).ConfigureAwait(false)) { return false; }
                    await RequireCurrent(admitted).ConfigureAwait(false);
                    return true;
                }, Current, token).ConfigureAwait(false);
                if (result.Conflict)
                { throw new InvalidOperationException("The current source conflicted at publication. Restore is retired, not retried or overwritten; explicitly inspect/reconcile."); }
                return result;
            }
            finally { catalogue.Changed -= Observe; }
        }
        return Run();
    }
}