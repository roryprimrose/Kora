using System.ComponentModel;

using Kora.Application.Infrastructure;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed class MicrophoneRecoveryViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel host;
    private readonly CancellationTokenSource lifetime = new();
    private MicrophoneRecoveryChoice[] choices = [];
    private MicrophoneRecoveryChoice? draft;
    private MicrophoneRecoveryChoice? displayedSelection;
    private string status = "Refresh devices to review current metadata. No recording is opened.";
    private bool busy;
    private bool disposed;

    public MicrophoneRecoveryViewModel(MainViewModel host)
    {
        this.host = host;
        host.PropertyChanged += OnHostChanged;
        Update();
    }

    public IReadOnlyList<MicrophoneRecoveryChoice> Choices => choices;
    public MicrophoneRecoveryChoice? DisplayedSelection => displayedSelection;
    public string Status => status;
    public string Readiness => host.TrayInputStatus + ". " + host.ListeningStatus;
    public string Consent => host.HasVoiceConsent
        ? "Saved ongoing voice consent is retained. Selection does not enable listening."
        : "Ongoing voice consent is absent or withdrawn. Enable is unavailable; review consent separately in speech Settings.";
    public const string Limitations = "Passive device recovery, not a durable question or approval. "
        + "No session/task answer is created. No combined consent, selection and enable, microphone test, "
        + "wake listening, model, network or Windows permission change is provided here. "
        + "Enable only arms existing push-to-talk readiness; capture still requires a separate held push-to-talk input.";
    public bool CanRefresh => !disposed && !busy && host.CanUseTrayMicrophoneRecovery;
    public bool CanSave => CanRefresh && !host.IsBusy && IsCurrent(draft) && draft!.IsAvailable;
    public bool CanEnable => CanRefresh && !host.IsBusy && IsCurrent(displayedSelection) && displayedSelection!.IsAvailable
        && (draft is null || draft == displayedSelection)
        && host.HasVoiceConsent && !host.IsVoiceEnabled && host.IsVoiceActivationAvailable
        && host.MicrophoneAccessStatus.State == MicrophoneAccessState.Allowed
        && (!displayedSelection.Device.IsSystemDefault || host.IsSystemMicrophoneAvailable)
        && host.ToggleListeningCommand.CanExecute(null);

    public MicrophoneRecoveryChoice? Draft
    {
        get => draft;
        set { SetProperty(ref draft, value); Notify(); }
    }

    public Task RefreshAsync() => RunAsync(async () =>
    {
        await host.RefreshMicrophonesAsync();
        if (!disposed)
        {
            status = host.IsMicrophoneCatalogCurrent
                ? "Current metadata reviewed. Choose explicitly, then save preference only; no capture opened."
                : host.ResponseTitle + " " + host.ResponseBody;
        }
    });

    public Task SaveAsync(MicrophoneRecoveryChoice? choice) => RunAsync(async () =>
    {
        if (!IsCurrent(choice) || !choice!.IsAvailable)
        {
            status = "Selection changed or is unavailable. Refresh devices and make a fresh choice.";
            return;
        }
        var saved = await host.SelectMicrophoneFromRecoveryAsync(choice.Device, choice.Revision, lifetime.Token);
        if (disposed) { return; }
        status = saved
            ? "Saved preference only; no capture opened. Enable requires a separate fresh input."
            : "Selection was not saved. " + host.ResponseTitle + " " + host.ResponseBody;
    });

    public Task EnableAsync(MicrophoneRecoveryChoice? choice) => RunAsync(async () =>
    {
        if (!IsCurrent(choice) || choice != displayedSelection || !host.HasVoiceConsent
            || draft is not null && draft != displayedSelection)
        {
            status = "Enable denied: review the current saved endpoint and consent, then use fresh explicit input.";
            return;
        }
        await host.EnableListeningFromRecoveryAsync(choice!.Revision, choice.Device, lifetime.Token);
        if (disposed) { return; }
        status = host.ResponseTitle + " " + host.ResponseBody;
    });

    public Task DisableAsync() => RunAsync(host.DisableListeningFromTrayAsync, stop: true);
    public Task StopSpeakingAsync() => RunAsync(host.StopSpeakingFromTrayAsync, stop: true);

    private bool IsCurrent(MicrophoneRecoveryChoice? choice) => !disposed && choice is not null
        && choices.Any(item => ReferenceEquals(item, choice)) && host.CanUseTrayMicrophoneRecovery
        && host.IsMicrophoneCatalogCurrent && !host.IsRefreshingMicrophones
        && choice.Revision == host.MicrophoneTopologyRevision && choice.CallRevision == host.CallPolicyRevision;

    private async Task RunAsync(Func<Task> action, bool stop = false)
    {
        if (disposed) { return; }
        if (stop)
        {
            await action();
            if (!disposed) { Update(); }
            return;
        }
        if (busy || !host.CanUseTrayMicrophoneRecovery)
        {
            status = "Recovery is unavailable. Return to the owning unlocked host and refresh devices.";
            Update();
            return;
        }
        busy = true;
        Notify();
        try
        {
            await action();
        }
        finally
        {
            busy = false;
            if (!disposed) { Update(); }
        }
    }

    private void OnHostChanged(object? sender, PropertyChangedEventArgs args) => Update();

    private void Update()
    {
        if (disposed) { return; }
        if (!host.CanUseTrayMicrophoneRecovery || !host.IsMicrophoneCatalogCurrent || host.IsRefreshingMicrophones)
        {
            choices = [];
            displayedSelection = null;
            draft = null;
            status = host.IsRefreshingMicrophones ? "Refreshing metadata (up to five seconds); no capture."
                : "Current metadata or ownership/privacy is unavailable. " + host.ResponseTitle
                    + " Refresh devices in the owning unlocked host; review Windows microphone permission manually.";
        }
        else if (choices.Length == 0 || choices[0].Revision != host.MicrophoneTopologyRevision
            || choices[0].CallRevision != host.CallPolicyRevision)
        {
            var devices = host.Microphones.ToList();
            if (host.SelectedMicrophone is { } pin && !devices.Contains(pin)) { devices.Add(pin); }
            choices = devices.Select(device => new MicrophoneRecoveryChoice(device,
                host.MicrophoneTopologyRevision, host.CallPolicyRevision,
                host.Microphones.Contains(device),
                device.IsSystemDefault
                    ? host.SystemMicrophone is { } currentDefault
                        ? "System - Windows default: " + currentDefault.Name + " [" + currentDefault.Id + "]; available"
                        : "System - Windows default; unavailable"
                    : device.Name + " [" + device.Id + "] - "
                        + (host.Microphones.Contains(device) ? "available" : "unavailable; saved pin retained"))).ToArray();
            displayedSelection = choices.FirstOrDefault(choice => choice.Device == host.SelectedMicrophone);
            draft = null;
        }
        Notify();
    }

    private void Notify()
    {
        foreach (var name in new[] { nameof(Choices), nameof(Draft), nameof(DisplayedSelection),
            nameof(Status), nameof(Readiness), nameof(Consent), nameof(CanRefresh), nameof(CanSave), nameof(CanEnable) })
        {
            OnPropertyChanged(name);
        }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        host.PropertyChanged -= OnHostChanged;
        lifetime.Cancel();
        // The token remains readable by an already admitted finite host operation.
        lifetime.Dispose();
        choices = [];
        draft = null;
        displayedSelection = null;
        status = "Closed without consent, task cancellation, session completion or a question answer.";
        Notify();
    }
}
