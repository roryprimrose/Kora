using System.Text.Json;
using Kora.Application.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionProviderDraft? providerDraft;
    private SessionProviderProposal? providerProposal;
    private long providerEpoch;
    private ProviderService providerService;
    private ProviderCatalogueModel? providerModel;
    private ProviderProfileOption? providerReasoning;
    private ProviderProfileOption? providerContext;
    private ProviderCatalogueModel? providerFieldModel;
    private ProviderProfileOption? providerFieldReasoning;
    private ProviderProfileOption? providerFieldContext;
    private string providerStatus = "Select an exact session and Refresh. All four fields initially inherit app settings; no app fallback, authentication or model call.";
    private bool providerSurfaceOpen = true;
    public void SetProviderSurfaceOpen(bool open)
    {
        providerSurfaceOpen = open;
        if (!open) { ClearProviderReview(); }
    }
    public IReadOnlyList<ProviderService> ProviderServices { get; } = Enum.GetValues<ProviderService>();
    public ProviderService ProviderService { get => providerService; set { providerService = value; InvalidateProviderProposal(); OnPropertyChanged(); } }
    public IReadOnlyList<ProviderCatalogueModel> ProviderModels => providerDraft?.Catalogue?.Models ?? [];
    public ProviderCatalogueModel? ProviderModel
    {
        get => providerModel;
        set
        {
            providerModel = value;
            providerReasoning = null;
            providerContext = null;
            InvalidateProviderProposal();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProviderReasonings));
            OnPropertyChanged(nameof(ProviderContexts));
            OnPropertyChanged(nameof(ProviderReasoning));
            OnPropertyChanged(nameof(ProviderContext));
            OnPropertyChanged(nameof(CanReviewProviderModel));
        }
    }
    public IReadOnlyList<ProviderProfileOption> ProviderReasonings => providerModel?.Reasoning ?? [];
    public IReadOnlyList<ProviderProfileOption> ProviderContexts => providerModel?.Context ?? [];
    public ProviderProfileOption? ProviderReasoning { get => providerReasoning; set { providerReasoning = value; InvalidateProviderProposal(); OnPropertyChanged(); OnPropertyChanged(nameof(CanReviewProviderModel)); } }
    public ProviderProfileOption? ProviderContext { get => providerContext; set { providerContext = value; InvalidateProviderProposal(); OnPropertyChanged(); OnPropertyChanged(nameof(CanReviewProviderModel)); } }
    public ProviderCatalogueModel? ProviderFieldModel { get => providerFieldModel; set { providerFieldModel = value; InvalidateProviderProposal(); OnPropertyChanged(); OnPropertyChanged(nameof(CanReviewProviderModelField)); } }
    public ProviderProfileOption? ProviderFieldReasoning { get => providerFieldReasoning; set { providerFieldReasoning = value; InvalidateProviderProposal(); OnPropertyChanged(); OnPropertyChanged(nameof(CanReviewProviderReasoningField)); } }
    public ProviderProfileOption? ProviderFieldContext { get => providerFieldContext; set { providerFieldContext = value; InvalidateProviderProposal(); OnPropertyChanged(); OnPropertyChanged(nameof(CanReviewProviderContextField)); } }
    private ProviderCatalogueModel? EffectiveProviderModel => providerDraft?.Catalogue?.Models.SingleOrDefault(model => model.Id == providerDraft.Effective?.Model);
    public IReadOnlyList<ProviderProfileOption> ProviderFieldReasonings => EffectiveProviderModel?.Reasoning ?? [];
    public IReadOnlyList<ProviderProfileOption> ProviderFieldContexts => EffectiveProviderModel?.Context ?? [];
    public bool CanSelectProviderField => CanReviewProviders && providerDraft?.Effective is not null;
    public bool CanReviewProviderModelField => CanSelectProviderField && ProviderFieldModel is not null;
    public bool CanReviewProviderReasoningField => CanSelectProviderField && ProviderFieldReasoning is not null;
    public bool CanReviewProviderContextField => CanSelectProviderField && ProviderFieldContext is not null;
    public bool CanInspectProviders => CanRead && providers is not null && selected is not null;
    public bool CanReviewProviders => CanInspectProviders && access.CanControl && selected?.Authority.IsActive == true
        && providerDraft is { Saved.Held: false } && providerEpoch == selectionEpoch && providerDraft.Saved.Session == selected.Authority;
    public bool CanConfirmProviders => CanReviewProviders && providerProposal is not null;
    public bool CanSelectProviderModel => CanReviewProviders && ProviderModels.Count != 0;
    public bool CanReviewProviderModel => CanReviewProviders && ProviderModels.Count != 0 && ProviderModel is not null
        && ProviderReasoning is not null && ProviderContext is not null;
    public string ProviderStatus => providerEpoch == selectionEpoch && providerDraft?.Saved.Session == selected?.Authority
        ? providerStatus : "Provider review unavailable/stale: select an exact existing session and Refresh; no app fallback.";

    public Task RefreshProviders() => RunAsync(async () =>
    {
        ClearProviderReview();
        var target = RequireSelected().Authority;
        providerEpoch = selectionEpoch;
        var epoch = providerEpoch;
        var draft = await providers!.Inspect(target.SessionId, RequestOrigin.LocalUi,
            () => !closed && providerSurfaceOpen && access.CanInspect && selectionEpoch == epoch && selected?.Authority == target, lifetime.Token);
        if (closed || selectionEpoch != epoch || selected?.Authority != target) { providers.Discard(draft); return; }
        providerDraft = draft;
        ProviderService = draft.EffectiveService;
        ProviderModel = null;
        providerStatus = JsonSerializer.Serialize(new
        {
            scope = "This exact session only", draft.Saved, inherited = new
            {
                service = draft.Saved.Desired.Service is null, model = draft.Saved.Desired.Model is null,
                reasoning = draft.Saved.Desired.Reasoning is null, context = draft.Saved.Desired.Context is null,
            },
            draft.App, draft.Effective, draft.Status, catalogue = Provenance(draft),
        });
        OnPropertyChanged(nameof(ProviderModels));
        NotifyProviderReview();
    });

    public Task ReviewProviderField(SessionProviderField? field, bool reset) => RunAsync(() =>
    {
        var draft = providerDraft ?? throw new InvalidOperationException("Refresh exact session provider policy first.");
        if (!access.CanControl || providerEpoch != selectionEpoch || draft.Saved.Session != selected?.Authority)
        { throw new InvalidOperationException("The native selected session, generation or review changed."); }
        providerProposal = reset ? providers!.Reset(draft, field)
            : field == SessionProviderField.Service ? providers!.SelectService(draft, ProviderService)
            : field is { } selectedField ? providers!.SelectField(draft, selectedField, selectedField switch
            {
                SessionProviderField.Model => ProviderFieldModel?.Id,
                SessionProviderField.Reasoning => ProviderFieldReasoning?.Id,
                SessionProviderField.Context => ProviderFieldContext?.Id,
                _ => null,
            } ?? throw new InvalidOperationException("Choose one exact advertised field. Missing/unavailable effective tuples require complete-profile review."))
            : providers!.Select(draft, field,
                ProviderModel ?? throw new InvalidOperationException("No current exact model selection. Production has no qualified catalogue."),
                ProviderReasoning ?? throw new InvalidOperationException("Choose exact advertised reasoning."),
                ProviderContext ?? throw new InvalidOperationException("Choose exact advertised context."));
        providerStatus = JsonSerializer.Serialize(new { scope = "Review only: this exact session; app preferences never written",
            session = draft.Saved.Session, revision = draft.Saved.Revision, saved = draft.Saved.Desired,
            proposed = providerProposal.Desired, field = providerProposal.Field?.ToString(), providerProposal.PreviousEffective,
            providerProposal.ResultingEffective, providerProposal.AppRevision, draft.Status, catalogue = Provenance(draft) });
        NotifyProviderReview();
        return Task.CompletedTask;
    });

    public Task ConfirmProviders() => RunAsync(async () =>
    {
        var proposal = providerProposal ?? throw new InvalidOperationException("Review one exact provider proposal first.");
        var target = RequireSelected().Authority;
        var epoch = selectionEpoch;
        var saved = await providers!.Confirm(proposal, RequestOrigin.LocalUi,
            () => !closed && providerSurfaceOpen && access.CanControl && selectionEpoch == epoch && selected?.Authority == target, lifetime.Token);
        ClearProviderReview();
        status = "Exact session provider override committed; app defaults unchanged, no authentication, network, generation or egress. Refresh for saved/effective status.";
        detail = JsonSerializer.Serialize(saved);
    });

    public void ClearProviderReview()
    {
        if (providerDraft is { } draft) { providers?.Discard(draft); }
        providerDraft = null;
        providerProposal = null;
        providerModel = null;
        providerReasoning = null;
        providerContext = null;
        providerFieldModel = null;
        providerFieldReasoning = null;
        providerFieldContext = null;
        NotifyProviderReview();
    }

    private void InvalidateProviderProposal()
    {
        providerProposal = null;
        OnPropertyChanged(nameof(CanConfirmProviders));
    }

    private void NotifyProviderReview()
    {
        OnPropertyChanged(nameof(ProviderStatus));
        OnPropertyChanged(nameof(CanInspectProviders));
        OnPropertyChanged(nameof(CanReviewProviders));
        OnPropertyChanged(nameof(CanConfirmProviders));
        OnPropertyChanged(nameof(CanReviewProviderModel));
        OnPropertyChanged(nameof(CanSelectProviderModel));
        OnPropertyChanged(nameof(ProviderFieldModel));
        OnPropertyChanged(nameof(ProviderFieldReasoning));
        OnPropertyChanged(nameof(ProviderFieldContext));
        OnPropertyChanged(nameof(ProviderFieldReasonings));
        OnPropertyChanged(nameof(ProviderFieldContexts));
        OnPropertyChanged(nameof(CanSelectProviderField));
        OnPropertyChanged(nameof(CanReviewProviderModelField));
        OnPropertyChanged(nameof(CanReviewProviderReasoningField));
        OnPropertyChanged(nameof(CanReviewProviderContextField));
    }
    private static ProviderCatalogueProvenance? Provenance(SessionProviderDraft draft) =>
        draft.Catalogue is { } current ? new(current.Service, current.Account, current.Revision,
            current.Status, current.ObservedAt, current.ValidUntil, current.Models.Count) : null;

    private sealed record ProviderCatalogueProvenance(ProviderService Service, ProviderAccountId? Account,
        HostRevision Revision, ProviderCatalogueStatus Status, DateTimeOffset ObservedAt, DateTimeOffset ValidUntil, int ModelCount);
}
