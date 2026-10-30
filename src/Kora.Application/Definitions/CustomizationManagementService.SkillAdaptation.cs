using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public sealed partial class CustomizationManagementService
{
    public async Task<DeclarativeSkillAdaptation> PrepareSkillAdaptation(CustomizationSnapshot original, string name,
        Func<bool> eligible, CancellationToken token)
    {
        var destination = await Create(CustomizationKind.Skill, CustomizationScope.Kora, name, eligible, token).ConfigureAwait(false);
        var adaptation = DeclarativeSkillAdaptation.Create(original, destination);
        ValidateAdaptation(adaptation, [adaptation.Proposed]);
        await RequireUnchangedSource(original.Source, original, eligible, token).ConfigureAwait(false);
        return adaptation;
    }

    public Task<CustomizationSnapshot?> SaveSkillAdaptation(DeclarativeSkillAdaptation adaptation,
        IReadOnlyList<CustomizationFile> files, Func<CancellationToken, Task<bool>> review,
        Func<CancellationToken, Task<bool>> confirm, Func<bool> eligible, TimeProvider time, CancellationToken token)
    {
        var proposed = files.ToArray();
        ValidateAdaptation(adaptation, proposed);
        var started = time.GetUtcNow();
        var expires = started.AddMinutes(2);
        bool Current() => eligible() && time.GetUtcNow() >= started && time.GetUtcNow() < expires;
        async Task Validate(CancellationToken admitted)
        {
            Require(Current, admitted);
            ValidateAdaptation(adaptation, proposed);
            await RequireUnchangedSourceCore(adaptation.Original.Source, adaptation.Original, Current, admitted).ConfigureAwait(false);
            await RequireUnchangedSourceCore(adaptation.Destination, null, Current, admitted).ConfigureAwait(false);
        }
        return Save(adaptation.Destination, null, proposed, async admitted =>
        {
            if (!await review(admitted).ConfigureAwait(false)) { return false; }
            await Validate(admitted).ConfigureAwait(false);
            return await confirm(admitted).ConfigureAwait(false);
        }, Current, token, Validate, adaptation.Original);
    }

    private void ValidateAdaptation(DeclarativeSkillAdaptation adaptation, IReadOnlyList<CustomizationFile> files)
    {
        if (catalogue.IsReloadUnavailable)
        { throw new InvalidOperationException("The definition catalogue is unavailable. Reconcile and inspect the source again."); }
        ArtifactPublicationValidation.RequireUnshadowed(adaptation.ValidateDraft(files).Definition,
            catalogue.Candidates, new BuiltInCommandRouter(new BuiltInCommandCatalog()));
    }
}
