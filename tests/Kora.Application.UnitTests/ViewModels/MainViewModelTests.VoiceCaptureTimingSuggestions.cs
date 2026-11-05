using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("privacy")]
    [InlineData("consent")]
    [InlineData("source")]
    [InlineData("owner")]
    [InlineData("name")]
    [InlineData("device")]
    [InlineData("call")]
    [InlineData("dispose")]
    public async Task RetainedCaptureTimingUsesNativeWriterAndRejectsChangedHostBindingsWithoutAudio(string transition)
    {
        var f = new Fixture(enableCaptureTiming: true);
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        var (store, adapter) = BindSpeechSuggestions(f);
        adapter.Answer = "set voice.trailing-silence to 2.5000001";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        var current = true;
        var starts = f.Voice.StartCalls;
        VoiceCaptureTimingSuggestionReview review;
        using (var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()),
            RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            review = await model.ReviewVoiceCaptureTimingSuggestionAsync(message, () => current, _ => Task.CompletedTask,
                TestContext.Current.CancellationToken);
        }
        using (review)
        {
            switch (transition)
            {
                case "privacy": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; break;
                case "consent": await model.SetVoiceConsentAsync(true); break;
                case "source": current = false; break;
                case "owner": model.BindCallOwnershipGate(static () => false); break;
                case "name": await model.SetAssistantNameAsync("Nova"); break;
                case "device": model.SelectedMicrophone = new("other", "Other microphone"); break;
                case "call": f.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
                case "dispose": model.Dispose(); break;
            }
            using var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()),
                RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
            if (transition is "save")
            {
                (await model.ConfirmVoiceCaptureTimingSuggestionAsync(review, TestContext.Current.CancellationToken)).Should().BeTrue();
                f.CaptureTimingPreferences.Value.Should().Be(new VoiceCaptureTiming(5, 15, 2.5000001m));
                model.VoiceTimingStatus.Should().Contain("2.5000001 s");
            }
            else
            {
                await model.Invoking(vm => vm.ConfirmVoiceCaptureTimingSuggestionAsync(review, TestContext.Current.CancellationToken))
                    .Should().ThrowAsync<InvalidOperationException>();
                f.CaptureTimingPreferences.Writes.Should().Be(0);
            }
        }
        f.Voice.StartCalls.Should().Be(starts);
        adapter.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        f.Preferences.SavedVoiceId.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureTimingReviewAndConfirmationRemainOwnedUntilAwaitedSourceCheckFinishes(bool confirming)
    {
        var f = new Fixture(enableCaptureTiming: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var (store, adapter) = BindSpeechSuggestions(f);
        adapter.Answer = "set voice.trailing-silence to 2";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        using var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var waiting = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Validate(CancellationToken _)
        {
            if (waiting) { entered.TrySetResult(); await release.Task; }
        }
        using var review = await f.ViewModel.ReviewVoiceCaptureTimingSuggestionAsync(message, static () => true, Validate,
            TestContext.Current.CancellationToken);
        using var confirmation = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        waiting = true;
        Task work = confirming ? f.ViewModel.ConfirmVoiceCaptureTimingSuggestionAsync(review, TestContext.Current.CancellationToken)
            : f.ViewModel.ReviewVoiceCaptureTimingSuggestionAsync(message, static () => true, Validate, TestContext.Current.CancellationToken);
        await entered.Task;
        await f.ViewModel.Invoking(vm => vm.ReviewVoiceCaptureTimingSuggestionAsync(message, static () => true, Validate,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.Invoking(vm => vm.ConfirmVoiceCaptureTimingSuggestionAsync(review, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.ViewModel.Dispose();
        var disposal = f.ViewModel.DisposeAsync().AsTask();
        work.IsCompleted.Should().BeFalse();
        disposal.IsCompleted.Should().BeFalse();
        release.SetResult();
        await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => disposal).Should().ThrowAsync<InvalidOperationException>();
        f.CaptureTimingPreferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CaptureTimingSuggestionsRequireAvailableMatchingOwnerAndNeedNoSpeechBinding()
    {
        var f = new Fixture(enableCaptureTiming: true);
        var unavailable = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var unavailableAdmission = unavailable.OutputAdmission;
        await using var model = f.ViewModel;
        await using var unavailableModel = unavailable.ViewModel;
        await model.InitializeAsync();
        await unavailableModel.InitializeAsync();
        var (store, adapter) = BindSpeechSuggestions(f, bindSpeech: false);
        adapter.Answer = "reset voice.reply-start-wait";
        await SubmitSpeechSuggestion(f, store, voice: false);
        var message = store.RequestMessage!;
        using var input = HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        await unavailableModel.Invoking(vm => vm.ReviewVoiceCaptureTimingSuggestionAsync(message, static () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
        using var review = await model.ReviewVoiceCaptureTimingSuggestionAsync(message, static () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        await unavailableModel.Invoking(vm => vm.ConfirmVoiceCaptureTimingSuggestionAsync(review, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        model.Dispose();
        await model.Invoking(vm => vm.ReviewVoiceCaptureTimingSuggestionAsync(message, static () => true,
            _ => Task.CompletedTask, TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidOperationException>();
    }
}
