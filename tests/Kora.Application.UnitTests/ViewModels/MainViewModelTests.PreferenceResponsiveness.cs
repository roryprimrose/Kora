using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task StalledPlacementCommitYieldsAndKeepsOnlyTheLatestQueuedPositionWithoutLosingWindowOptions()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        var calls = 0;
        fixture.AppearancePreferences.BeforePresencePreferenceSave = () =>
        {
            if (Interlocked.Increment(ref calls) != 1) { return; }
            entered.SetResult();
            release.Wait(token);
        };
        fixture.ViewModel.QueueResponseWindowPosition(new(1, 2));
        try
        {
            await entered.Task.WaitAsync(token);
            fixture.ViewModel.PreferenceUpdate.IsCompleted.Should().BeFalse();
            fixture.ViewModel.ResponseWindowPosition.Should().BeNull();
            fixture.ViewModel.QueueResponseWindowPosition(new(3, 4));
            fixture.ViewModel.QueueResponseWindowPosition(new(5, 6));
            fixture.ViewModel.IsResponseAlwaysVisible = true;
            fixture.ViewModel.IsResponseWindowTopmost = false;
            var settingsOpened = false;
            fixture.ViewModel.SettingsRequested += (_, _) => settingsOpened = true;
            fixture.ViewModel.ShowSettings();
            settingsOpened.Should().BeTrue();
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        }
        finally { release.Set(); }
        await fixture.ViewModel.PreferenceUpdate;
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().Be(new ResponseWindowSettings(true, false, new(5, 6)));
        calls.Should().Be(4);
    }

    [Fact]
    public async Task QueuedPlacementCannotPersistAfterTheOriginalPrivacyEpochChanges()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested
                || !string.Equals(audit.ActionId, "configuration.presence-position", StringComparison.Ordinal)) { return; }
            entered.SetResult();
            release.Wait(token);
        };
        fixture.ViewModel.QueuePresencePosition(new(10, 20));
        try
        {
            await entered.Task.WaitAsync(token);
            fixture.ViewModel.QueueResponseWindowPosition(new(30, 40));
            fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 123 };
        }
        finally { release.Set(); }
        await fixture.ViewModel.PreferenceUpdate;
        fixture.AppearancePreferences.SavedPresencePosition.Should().BeNull();
        fixture.AppearancePreferences.SavedResponseWindowSettings.Should().BeNull();
    }

    [Fact]
    public async Task ForcedDisposalJoinsOwnedPreferencePersistenceWithoutWaitingForADispatcherContinuation()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.AppearancePreferences.BeforePresencePreferenceSave = () =>
        {
            entered.SetResult();
            release.Wait(token);
        };
        fixture.ViewModel.QueuePresencePosition(new(10, 20));
        await entered.Task.WaitAsync(token);
        var dispose = fixture.ViewModel.DisposeAsync().AsTask();
        try { dispose.IsCompleted.Should().BeFalse(); }
        finally { release.Set(); }
        await dispose;
        await fixture.ViewModel.PreferenceUpdate;
        fixture.ViewModel.PresencePosition.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SlowOutputDiscoveryDoesNotBlockResponseStateOrSettingsAndNeverSpeaksAfterLock(bool lockHost)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.TextToSpeech.BeforeOutputEnumeration = () =>
        {
            entered.SetResult();
            release.Wait(token);
        };
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.WindowActions.Clear();
        var command = fixture.RunAsync("what power action is pending");
        try
        {
            await entered.Task.WaitAsync(token);
            command.IsCompleted.Should().BeFalse();
            fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
            fixture.TextToSpeech.SpokenText.Should().BeNull();
            fixture.WindowActions.Should().BeEmpty();
            fixture.ViewModel.ShowSettings();
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
            if (lockHost) { fixture.Session.IsUnlocked = false; }
        }
        finally { release.Set(); }
        await command;
        if (lockHost)
        {
            fixture.TextToSpeech.SpokenText.Should().BeNull();
            fixture.WindowActions.Should().BeEmpty();
        }
        else
        {
            fixture.ViewModel.ResponseTitle.Should().Be("No power action is pending.");
            fixture.ViewModel.IsSpeechResponseEnabled.Should().BeTrue(fixture.ViewModel.ResponseBody);
            fixture.TextToSpeech.SpokenText.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task PendingModelPreferenceRequiresExplicitNameAndCannotGrantEarlyModelAdmission()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var token = TestContext.Current.CancellationToken;
        fixture.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested
                || !string.Equals(audit.ActionId, "configuration.model-approval", StringComparison.Ordinal)) { return; }
            entered.SetResult();
            release.Wait(token);
        };
        fixture.ViewModel.RequireAssistantNameForVoiceApproval = false;
        try
        {
            await entered.Task.WaitAsync(token);
            fixture.ViewModel.PreferenceUpdate.IsCompleted.Should().BeFalse();
            fixture.ViewModel.RequireAssistantNameForVoiceApproval.Should().BeTrue();
            fixture.ViewModel.LocalModelsEnabled.Should().BeFalse();
            fixture.ApprovalPreferences.Preferences.RequireAssistantNameForVoiceApproval.Should().BeTrue();
            (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        }
        finally { release.Set(); }
        await fixture.ViewModel.PreferenceUpdate;
        fixture.ViewModel.RequireAssistantNameForVoiceApproval.Should().BeFalse();
        fixture.ViewModel.LocalModelsEnabled.Should().BeTrue();
    }
}
