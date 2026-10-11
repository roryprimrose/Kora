using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly JsonSerializerOptions ProviderProfileJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter<ProviderService>(), new JsonStringEnumConverter<LocalProviderInstallation>(),
            new JsonStringEnumConverter<Kora.Core.Dependencies.ModelProviderIdentity>(), new JsonStringEnumConverter<ProviderProfileField>() },
    };
    private readonly ProviderProfileConfigurationService? providerProfileConfiguration;
    private ProviderProfileChoice? selectedProviderServiceChoice;
    private ProviderCatalogueModel? selectedProviderProfileModel;
    private ProviderProfileOption? selectedProviderReasoning;
    private ProviderProfileOption? selectedProviderContext;
    private bool providerProfileControlActive;
    private ProviderProfileChoice? providerFieldPreview;
    private long providerFieldPreviewCallRevision;
    private long providerServiceDraftRevision;
    private long providerFieldPreviewSelectionRevision;
    private ProviderProfileOption? selectedProviderReasoningField;
    private ProviderProfileOption? selectedProviderContextField;

    public AsyncCommand RefreshProviderProfileCommand { get; }
    public AsyncCommand SaveProviderServiceCommand { get; }
    public AsyncCommand ResetProviderServiceCommand { get; }
    public AsyncCommand SaveProviderProfileCommand { get; }
    public AsyncCommand ResetProviderProfileCommand { get; }
    public AsyncCommand ProviderSignInCommand { get; }
    public AsyncCommand ReviewProviderModelCommand { get; }
    public AsyncCommand ReviewProviderReasoningCommand { get; }
    public AsyncCommand ReviewProviderContextCommand { get; }
    public AsyncCommand ConfirmProviderFieldCommand { get; }
    public AsyncCommand CancelProviderFieldCommand { get; }
    public Guid? ProviderFieldConfirmationId => providerFieldPreview?.Confirmation;
    public bool CanConfirmProviderField => CanChangeProviderProfile && providerFieldPreview is not null;
    public IReadOnlyList<ProviderProfileOption> ProviderReasoningFieldChoices => SavedProviderModel?.Reasoning ?? [];
    public IReadOnlyList<ProviderProfileOption> ProviderContextFieldChoices => SavedProviderModel?.Context ?? [];
    private ProviderCatalogueModel? SavedProviderModel => SelectedProviderServiceChoice is { } choice
        ? providerProfileConfiguration?.SavedModelFor(choice) : null;
    public ProviderProfileOption? SelectedProviderReasoningField
    {
        get => selectedProviderReasoningField;
        set { if (SetProperty(ref selectedProviderReasoningField, value)) { OnPropertyChanged(nameof(CanReviewProviderReasoning)); } }
    }
    public ProviderProfileOption? SelectedProviderContextField
    {
        get => selectedProviderContextField;
        set { if (SetProperty(ref selectedProviderContextField, value)) { OnPropertyChanged(nameof(CanReviewProviderContext)); } }
    }
    public bool CanReviewProviderModel => CanChangeProviderProfile && SavedProviderModel is not null
        && SelectedProviderProfileModel is not null && ProviderProfileModels.Any(model => ReferenceEquals(model, SelectedProviderProfileModel));
    public bool CanReviewProviderReasoning => CanChangeProviderProfile && SelectedProviderReasoningField is not null
        && ProviderReasoningFieldChoices.Any(option => ReferenceEquals(option, SelectedProviderReasoningField));
    public bool CanReviewProviderContext => CanChangeProviderProfile && SelectedProviderContextField is not null
        && ProviderContextFieldChoices.Any(option => ReferenceEquals(option, SelectedProviderContextField));
    public string ProviderFieldPreviewStatus => providerFieldPreview is { } preview ? JsonSerializer.Serialize(new
    {
        scope = "App individual field review; no write before confirmation",
        field = preview.Field,
        service = preview.Tuple!.Service,
        savedTuple = preview.BaseTuple,
        proposedTuple = preview.Tuple,
        dependentValidity = "All retained exact IDs supported in this account/catalogue",
        preferenceRevision = preview.Revision,
        catalogue = providerProfileConfiguration!.CatalogueFor(preview),
        confirmation = preview.Confirmation,
    }, ProviderProfileJsonOptions) : "No pending app field review. Select an advertised exact field; a current saved complete tuple is required.";
    public IReadOnlyList<ProviderProfileChoice> ProviderServiceChoices => providerProfileConfiguration?.Choices ?? [];
    public IReadOnlyList<ProviderCatalogueModel> ProviderProfileModels => SelectedProviderServiceChoice is { } choice
        && providerProfileConfiguration is { } configuration ? configuration.ModelsFor(choice) : [];
    public IReadOnlyList<ProviderProfileOption> ProviderReasoningChoices => SelectedProviderProfileModel?.Reasoning ?? [];
    public IReadOnlyList<ProviderProfileOption> ProviderContextChoices => SelectedProviderProfileModel?.Context ?? [];
    public ProviderProfileChoice? SelectedProviderServiceChoice
    {
        get => selectedProviderServiceChoice;
        set
        {
            if (!SetProperty(ref selectedProviderServiceChoice, value)) { return; }
            providerServiceDraftRevision = checked(providerServiceDraftRevision + 1);
            SelectedProviderProfileModel = null;
            SelectedProviderReasoningField = null;
            SelectedProviderContextField = null;
            OnPropertyChanged(nameof(ProviderReasoningFieldChoices));
            OnPropertyChanged(nameof(ProviderContextFieldChoices));
            NotifyProviderFieldControls();
            OnPropertyChanged(nameof(ProviderProfileModels));
            OnPropertyChanged(nameof(CanSaveProviderService));
            OnPropertyChanged(nameof(ProviderProfileDraftStatus));
        }
    }
    public ProviderCatalogueModel? SelectedProviderProfileModel
    {
        get => selectedProviderProfileModel;
        set
        {
            if (!SetProperty(ref selectedProviderProfileModel, value)) { return; }
            SelectedProviderReasoning = null;
            SelectedProviderContext = null;
            OnPropertyChanged(nameof(ProviderReasoningChoices));
            OnPropertyChanged(nameof(ProviderContextChoices));
            OnPropertyChanged(nameof(CanSaveProviderProfile));
            OnPropertyChanged(nameof(ProviderProfileDraftStatus));
            OnPropertyChanged(nameof(CanReviewProviderModel));
        }
    }
    public ProviderProfileOption? SelectedProviderReasoning
    {
        get => selectedProviderReasoning;
        set
        {
            if (SetProperty(ref selectedProviderReasoning, value))
            { OnPropertyChanged(nameof(CanSaveProviderProfile)); OnPropertyChanged(nameof(ProviderProfileDraftStatus)); }
        }
    }
    public ProviderProfileOption? SelectedProviderContext
    {
        get => selectedProviderContext;
        set
        {
            if (SetProperty(ref selectedProviderContext, value))
            { OnPropertyChanged(nameof(CanSaveProviderProfile)); OnPropertyChanged(nameof(ProviderProfileDraftStatus)); }
        }
    }
    public bool CanChangeProviderProfile => providerProfileConfiguration is not null && !disposed && IsCallMutationHostEligible
        && !providerProfileControlActive && !IsResponseInteractionPending;
    public bool CanSignInProvider => false;
    public bool CanSaveProviderService => CanChangeProviderProfile && SelectedProviderServiceChoice is not null
        && ProviderServiceChoices.Any(choice => ReferenceEquals(choice, SelectedProviderServiceChoice));
    public bool CanSaveProviderProfile => CanChangeProviderProfile && SelectedProviderProfileModel is not null
        && SelectedProviderReasoning is not null && SelectedProviderContext is not null
        && ProviderProfileModels.Any(model => ReferenceEquals(model, SelectedProviderProfileModel));
    public string ProviderProfileDraftStatus
    {
        get
        {
            var current = SelectedProviderServiceChoice is { } choice ? providerProfileConfiguration?.CatalogueFor(choice) : null;
            return JsonSerializer.Serialize(new
            {
                scope = "App draft; nothing authenticated or executed",
                service = SelectedProviderServiceChoice?.Service,
                savedTuple = providerProfileConfiguration?.Get().Saved?.For(SelectedProviderServiceChoice?.Service ?? ProviderService.Unset),
                account = current?.Account,
                catalogueRevision = current?.Revision,
                catalogueStatus = current?.Status.ToString() ?? "Unavailable",
                availableModels = ProviderProfileModels.Count,
                model = SelectedProviderProfileModel?.Id,
                reasoning = SelectedProviderReasoning?.Id,
                context = SelectedProviderContext?.Id,
                current?.ObservedAt,
                current?.ValidUntil,
            }, ProviderProfileJsonOptions);
        }
    }
    public string ProviderProfileConfigurationStatus => providerProfileConfiguration is null
        ? "Unavailable: app provider profile control is not composed."
        : JsonSerializer.Serialize(providerProfileConfiguration.Get(), ProviderProfileJsonOptions);

    private void OnProviderProfileConfigurationChanged(object? sender, EventArgs args) => uiDispatcher.Post(SynchronizeProviderProfileConfiguration);
    private void SynchronizeProviderProfileConfiguration()
    {
        if (disposed) { return; }
        SelectedProviderServiceChoice = ProviderServiceChoices.FirstOrDefault(choice => choice.Service == providerProfileConfiguration!.Get().Desired?.Service);
        SelectedProviderProfileModel = null;
        OnPropertyChanged(nameof(ProviderServiceChoices));
        OnPropertyChanged(nameof(ProviderProfileModels));
        OnPropertyChanged(nameof(ProviderProfileConfigurationStatus));
        OnPropertyChanged(nameof(CanChangeProviderProfile));
        OnPropertyChanged(nameof(CanSaveProviderService));
        OnPropertyChanged(nameof(CanSaveProviderProfile));
        NotifyProviderFieldControls();
    }

    private void NotifyProviderFieldControls()
    {
        OnPropertyChanged(nameof(CanReviewProviderModel));
        OnPropertyChanged(nameof(CanReviewProviderReasoning));
        OnPropertyChanged(nameof(CanReviewProviderContext));
        OnPropertyChanged(nameof(CanConfirmProviderField));
        OnPropertyChanged(nameof(ProviderFieldConfirmationId));
        OnPropertyChanged(nameof(ProviderFieldPreviewStatus));
    }

    private Task ReviewNativeProviderField(ProviderProfileField field)
    {
        var id = field switch
        {
            ProviderProfileField.Model when CanReviewProviderModel => SelectedProviderProfileModel!.Id,
            ProviderProfileField.Reasoning when CanReviewProviderReasoning => SelectedProviderReasoningField!.Id,
            ProviderProfileField.Context when CanReviewProviderContext => SelectedProviderContextField!.Id,
            _ => throw new InvalidOperationException("Choose an exact advertised field for a current saved complete tuple; use the complete profile draft/save to recover."),
        };
        var choice = providerProfileConfiguration!.CreateFieldChoice(field, id, SelectedProviderServiceChoice);
        return ExecuteProviderProfileCommandAsync(new(AppearanceCommandOperation.Set, Field: field, FieldValue: id),
            SecurityAuditInitiator.LocalUser, choice);
    }

    private Task RunNativeProviderProfileAsync(ProviderProfileCommand command) =>
        ExecuteProviderProfileCommandAsync(command, SecurityAuditInitiator.LocalUser,
            command.Operation == AppearanceCommandOperation.Reset && command.TupleOnly && SelectedProviderServiceChoice is { } selected
                ? providerProfileConfiguration!.CreateResetChoice(tupleOnly: true, selected)
            : command.Operation != AppearanceCommandOperation.Set ? null : command.Service is not null
                ? SelectedProviderServiceChoice ?? throw new InvalidOperationException("Inspect and explicitly choose a desired app service.")
                : providerProfileConfiguration!.CreateTupleChoice(
                    SelectedProviderProfileModel ?? throw new InvalidOperationException("Choose a current exact model."),
                    SelectedProviderReasoning ?? throw new InvalidOperationException("Choose a current exact reasoning option."),
                    SelectedProviderContext ?? throw new InvalidOperationException("Choose a current exact context option."),
                    SelectedProviderServiceChoice));

    internal async Task ExecuteProviderProfileCommandAsync(ProviderProfileCommand command, SecurityAuditInitiator initiator,
        ProviderProfileChoice? nativeChoice = null, CancellationToken cancellationToken = default)
    {
        if (!CanChangeProviderProfile)
        {
            if (!disposed) { Transcript = "App provider control requires original user input in the current owning unlocked host and no pending interaction."; }
            return;
        }
        if (command.Operation == AppearanceCommandOperation.Clarify)
        { ShowFailure("App provider scope or exact choice required.", command.Error!); return; }
        var service = providerProfileConfiguration!;
        var origin = OriginalOrigin(initiator);
        if (initiator == SecurityAuditInitiator.VoiceCommand && HostActivity.Current is null) { origin = RequestOrigin.ActivatedVoice; }
        var remainsEligible = CaptureAudioControlEligibility(origin);
        bool Eligible() => !disposed && remainsEligible();
        var callRevision = CallPolicyRevision;
        providerProfileControlActive = true;
        OnPropertyChanged(nameof(CanChangeProviderProfile));
        NotifyProviderFieldControls();
        try
        {
            if (command.Confirmation is null)
            {
                providerFieldPreview = null;
                if (nativeChoice is null) { await service.RefreshAsync(origin, Eligible, cancellationToken); }
            }
            var saved = false;
            var reviewed = false;
            if (command.Confirmation is { } confirmation)
            {
                var preview = providerFieldPreview;
                if (preview is null || preview.Confirmation != confirmation || providerFieldPreviewSelectionRevision != providerServiceDraftRevision)
                { throw new InvalidOperationException("App field confirmation is missing, foreign or the selected service changed. Review the exact complete tuple again."); }
                providerFieldPreview = null;
                if (!command.Cancel)
                { saved = await service.SelectAsync(preview, providerFieldPreviewCallRevision, origin, initiator, communicationPolicy, Eligible, cancellationToken); }
            }
            else if (command.Field is { } field && command.Operation == AppearanceCommandOperation.Set)
            {
                var choice = nativeChoice ?? service.CreateFieldChoice(field,
                    command.FieldValue ?? throw new InvalidOperationException("An exact field ID is required."));
                if (nativeChoice is null && (!string.Equals(choice.Tuple!.Account.Value, command.Account, StringComparison.Ordinal)
                    || choice.Tuple.CatalogueRevision.Value != command.CatalogueRevision))
                { throw new InvalidOperationException("Account/catalogue preview changed; inspect and explicitly choose again."); }
                if (nativeChoice is null) { SynchronizeProviderProfileConfiguration(); }
                providerFieldPreview = choice;
                providerFieldPreviewCallRevision = callRevision;
                providerFieldPreviewSelectionRevision = providerServiceDraftRevision;
                reviewed = true;
            }
            else if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
            {
                var choice = nativeChoice;
                if (choice is null && command.Operation == AppearanceCommandOperation.Reset) { choice = service.CreateResetChoice(command.TupleOnly); }
                if (choice is null && command.Service is { } provider) { choice = service.Choices.Single(item => item.Service == provider); }
                if (choice is null)
                {
                    var model = service.Models.SingleOrDefault(item => string.Equals(item.Id.Value, command.Model, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException("No current advertised model matches that exact ID; no substitute was selected.");
                    var selection = service.CreateTupleChoice(model,
                        model.Reasoning.SingleOrDefault(item => string.Equals(item.Id.Value, command.Reasoning, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException("Choose an exact current reasoning ID."),
                        model.Context.SingleOrDefault(item => string.Equals(item.Id.Value, command.Context, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException("Choose an exact current context ID."));
                    if (!string.Equals(selection.Tuple!.Account.Value, command.Account, StringComparison.Ordinal) || selection.Tuple.CatalogueRevision.Value != command.CatalogueRevision)
                    { throw new InvalidOperationException("Account/catalogue preview changed; inspect and explicitly choose again."); }
                    choice = selection;
                }
                saved = await service.SelectAsync(choice, callRevision, origin, initiator, communicationPolicy, Eligible, cancellationToken);
            }
            if (!Eligible()) { throw new InvalidOperationException("App provider presentation admission changed."); }
            if (!reviewed) { SynchronizeProviderProfileConfiguration(); }
            var response = JsonSerializer.Serialize(new
            {
                scope = "App default only; exact session overrides use separate review; one-turn changes are unavailable",
                outcome = command.SignIn ? "Unavailable: no qualified sign-in adapter; no account credentials accessed"
                    : reviewed ? "Review exact field and whole resulting tuple; confirm or cancel using the host-issued ID; no write yet"
                    : command.Cancel ? "Cancelled; no write"
                    : saved ? "Desired configuration saved only; nothing authenticated, sent or executed"
                    : command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset ? "Denied; no confirmed write"
                    : "Observed; no execution authority",
                fieldReview = reviewed ? ProviderFieldPreviewStatus : null,
                state = service.Get(),
                models = service.Models,
                syntax = ProviderProfileCommand.Syntax,
            }, ProviderProfileJsonOptions);
            if (Encoding.UTF8.GetByteCount(response) > 65536) { throw new InvalidDataException("Provider discovery exceeds its complete 64 KiB output bound."); }
            if (IsSpeaking)
            {
                Transcript = response;
                PreserveSpokenResponseFailure("App provider result remains visual; nothing authenticated or executed.");
            }
            else
            {
                PresentResponse(AssistantState.Information, "App provider configuration only.", response, refreshOutput: false);
                forceVisualResponse = true;
                NotifyOutputPolicyChanged();
                if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
            or OperationCanceledException or TimeoutException or ArgumentOutOfRangeException)
        {
            providerFieldPreview = null;
            service.HoldUnavailable();
            ApplicationLog.Error(logger, exception, "Inspecting or saving app provider profiles");
            if (!disposed)
            {
                if (IsSpeaking || IsResponseInteractionPending)
                {
                    Transcript = "App provider configuration not confirmed. " + exception.Message;
                    PreserveSpokenResponseFailure("App provider configuration not confirmed; inspect saved state and receipts.");
                }
                else { ShowFailure("App provider configuration not confirmed.", exception.Message); }
            }
        }
        finally
        {
            providerProfileControlActive = false;
            if (providerFieldPreview is null) { SynchronizeProviderProfileConfiguration(); }
            OnPropertyChanged(nameof(CanChangeProviderProfile));
            NotifyProviderFieldControls();
        }
    }
}
