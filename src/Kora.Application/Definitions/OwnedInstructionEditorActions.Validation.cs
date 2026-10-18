using Kora.Core.Artifacts;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Definitions;

public sealed partial class OwnedInstructionEditorActions
{
    public bool IsSourceUnavailable => catalogue.IsReloadUnavailable;

    public Task<OwnedDraftValidation> ValidateDraft(string name, OwnedInstructionSnapshot? original, string draft,
        ILogger diagnosticLogger, Func<bool> eligible, CancellationToken token) =>
        ObserveDraft(name, original, draft, diagnosticLogger, eligible, static validation => validation, token);

    public Task<OwnedDraftComparison> CompareDraft(string name, OwnedInstructionSnapshot? original, string draft,
        ILogger diagnosticLogger, Func<bool> eligible, CancellationToken token) =>
        ObserveDraft(name, original, draft, diagnosticLogger, eligible, validation => new OwnedDraftComparison(original, draft, validation), token);

    private Task<T> ObserveDraft<T>(string name, OwnedInstructionSnapshot? original, string draft,
        ILogger diagnosticLogger, Func<bool> eligible, Func<OwnedDraftValidation, T> observe, CancellationToken token)
    {
        OwnedInstructionNames.WriteName(name);
        if (original is not null && (original.Document.Definition.Kind != Kind
            || !string.Equals(original.Name, name, StringComparison.Ordinal)
            || !string.Equals(original.CanonicalFileName, name + OwnedDefinitionKind.Extension(Kind), StringComparison.Ordinal)))
        { throw new InvalidDataException("Observation requires the exact canonical owned baseline ID and kind."); }
        var candidates = catalogue.Candidates;
        var failureRevision = catalogue.ReloadFailureRevision;
        var unavailable = catalogue.IsReloadUnavailable;
        bool Current() => eligible() && ReferenceEquals(candidates, catalogue.Candidates)
            && failureRevision == catalogue.ReloadFailureRevision && unavailable == catalogue.IsReloadUnavailable;
        return admission.Run(Current, async (request, session, current, admitted) =>
            await admission.Publish(request, session, current, () =>
            {
                return observe(ObserveDraftStructure(name, original, draft, diagnosticLogger, candidates, failureRevision, unavailable));
            }, admitted).ConfigureAwait(false), token);
    }

    private OwnedDraftValidation ObserveDraftStructure(string name, OwnedInstructionSnapshot? original, string draft,
        ILogger diagnosticLogger, IReadOnlyList<ArtifactDefinition> candidates, long failureRevision, bool unavailable)
    {
        ArtifactFileDocument? proposed = null;
        string? failure = null;
        try { proposed = PrepareSave(name, original, draft); }
        catch (InvalidDataException exception)
        {
            failure = exception.Message;
            DraftRejected(diagnosticLogger, "unsupported-structure");
        }
        catch (ArgumentException)
        {
            // Catalogue collision exceptions can contain a private declared spoken name.
            failure = "The proposal collides with observed artifact IDs, slash commands or spoken names, or the bounded catalogue is full.";
            DraftRejected(diagnosticLogger, "catalogue-collision");
        }
        return new OwnedDraftValidation(name, Kind, original?.Document.HasBom ?? false,
            proposed, failure, candidates, failureRevision, unavailable);
    }

    public bool IsDraftValidationCurrent(OwnedDraftValidation observation) =>
        observation.Kind == Kind && ReferenceEquals(observation.CatalogueIdentity, catalogue.Candidates)
        && observation.FailureRevision == catalogue.ReloadFailureRevision
        && observation.SourceUnavailable == catalogue.IsReloadUnavailable;
}
