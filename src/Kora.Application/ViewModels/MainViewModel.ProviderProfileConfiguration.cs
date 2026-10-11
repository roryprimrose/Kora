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
            new JsonStringEnumConverter<Kora.Core.Dependencies.ModelProviderIdentity>() },
    };
    private readonly ProviderProfileConfigurationService? providerProfileConfiguration;
    private ProviderProfileChoice? selectedProviderServiceChoice;
    private ProviderCatalogueModel? selectedProviderProfileModel;
    private ProviderProfileOption? selectedProviderReasoning;
    private ProviderProfileOption? selectedProviderContext;
    private bool providerProfileControlActive;

    public AsyncCommand RefreshProviderProfileCommand { get; }
    public AsyncCommand SaveProviderServiceCommand { get; }
    public AsyncCommand ResetProviderServiceCommand { get; }
    public AsyncCommand SaveProviderProfileCommand { get; }
    public AsyncCommand ResetProviderProfileCommand { get; }
    public AsyncCommand ProviderSignInCommand { get; }
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
            SelectedProviderProfileModel = null;
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
        try
        {
            if (nativeChoice is null) { await service.RefreshAsync(origin, Eligible, cancellationToken); }
            var saved = false;
            if (command.Operation is AppearanceCommandOperation.Set or AppearanceCommandOperation.Reset)
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
            SynchronizeProviderProfileConfiguration();
            var response = JsonSerializer.Serialize(new
            {
                scope = "App default; session overrides and one-turn changes are unavailable",
                outcome = command.SignIn ? "Unavailable: no qualified sign-in adapter; no account credentials accessed"
                    : saved ? "Desired configuration saved only; nothing authenticated, sent or executed" : "Observed; no execution authority",
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
            SynchronizeProviderProfileConfiguration();
        }
    }
}
