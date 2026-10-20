using System.ComponentModel;
using System.Globalization;

using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.Voice;

namespace Kora;

internal sealed partial class NewRequestViewModel : ObservableObject, IDisposable
{
    internal NewRequestComposer Composer { get; }
    internal ComposerDictationService? Dictation { get; }

    internal NewRequestViewModel(NewRequestComposer composer, ComposerDictationService? dictation = null)
    {
        Composer = composer;
        Dictation = dictation;
        composer.PropertyChanged += Changed;
        if (dictation is not null) { dictation.Changed += DictationChanged; }
    }

    public string DictationStatus => Dictation?.Status ?? "Literal dictation unavailable. Microphone closed.";
    public bool HasDictationReview => Dictation?.HasReview == true;
    public string DictationOriginal => Dictation?.OriginalBody ?? string.Empty;
    public string DictationLiteral => Dictation?.LiteralText ?? string.Empty;
    public string Body { get => Composer.Body; set => Composer.Body = value; }
    public string ReviewedBody => Composer.ReviewedBody;
    public string Status => Composer.Status;
    public string AssistantAnswer => Composer.AssistantResponse?.Body ?? string.Empty;
    public bool CanEdit => Composer.CanEdit;
    public bool CanCreate => Composer.CanCreate;
    public bool CanReview => Composer.CanReview;
    public bool CanSubmit => Composer.CanSubmit;
    public bool CanReviewSave => Composer.CanReviewSave;
    public bool CanSave => Composer.CanSave;
    public string SavedBodyReview => Composer.SavedBodyReview;
    public string ProposedSavedBody => Composer.ProposedSavedBody;
    public string BaselineBody => Composer.BaselineBody;
    public string SaveLabel => "Saved revision " + Composer.SavedRevision.ToString(CultureInfo.InvariantCulture)
        + (Composer.SavedRevision == 0 ? " (no saved body observed)" : " (exact observed/acknowledged snapshot)")
        + " | current buffer " + (Composer.Dirty ? "differs from confirmed baseline" : "matches confirmed baseline")
        + ". Save review and Submit review are separate one-shot operations.";
    public bool CanInspect => Composer.CanInspectAttempt;
    public string TargetLabel => Composer.Target is { } target
        ? "Exact created / observed target: " + target.Authority.SessionId.Value.ToString("D")
            + " | name: " + (target.Metadata?.Name.Value ?? "Unnamed")
            + " | generation " + target.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | metadata revision " + (target.Metadata?.Revision.Value ?? 0).ToString(CultureInfo.InvariantCulture)
            + " | " + (target.Authority.IsActive ? "Active" : "Done")
        : "No accepted target. Create commits only a new empty session; it never submits or saves this body.";
    public string AttemptLabel => Composer.Creation is { } creation
        ? "Original creation session " + creation.Request.SessionId.Value.ToString("D")
            + " | request " + creation.Request.RequestId.Value.ToString("D")
            + " | task " + creation.Request.TaskId.Value.ToString("D")
            + (Composer.Submission is { } submitted ? "\nSubmission request " + submitted.RequestId.Value.ToString("D")
                + " | task " + submitted.TaskId.Value.ToString("D") : string.Empty)
        : "No creation attempt. This memory draft is isolated from saved drafts, questions, approvals and general voice routing.";

    private void DictationChanged(object? sender, EventArgs args)
    {
        foreach (var name in new[] { nameof(DictationStatus), nameof(HasDictationReview), nameof(DictationOriginal), nameof(DictationLiteral) })
        { OnPropertyChanged(name); }
    }

    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (string.Equals(args.PropertyName, nameof(NewRequestComposer.DraftMeasurement), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(DraftSizeFeedback));
            return;
        }
        foreach (var name in new[] { nameof(Body), nameof(ReviewedBody), nameof(Status), nameof(AssistantAnswer), nameof(CanEdit),
            nameof(CanCreate), nameof(CanReview), nameof(CanSubmit), nameof(CanInspect), nameof(TargetLabel), nameof(AttemptLabel),
            nameof(CanReviewSave), nameof(CanSave), nameof(SavedBodyReview), nameof(ProposedSavedBody), nameof(BaselineBody), nameof(SaveLabel) })
        { OnPropertyChanged(name); }
    }

    public void Dispose()
    {
        Composer.Dispose();
        Composer.PropertyChanged -= Changed;
        if (Dictation is not null) { Dictation.Changed -= DictationChanged; }
    }
}
