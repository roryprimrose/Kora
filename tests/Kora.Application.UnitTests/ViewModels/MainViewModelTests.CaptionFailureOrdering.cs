using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptionFailurePreservesTrackedGrantPreparationInEitherDurableAuditOrder(bool grantCompletesFirst)
    {
        using var gates = new CaptionFailureAuditGates();
        var fixture = new Fixture(enableSpeechText: true, logger: gates);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.Audit.BeforeWrite = gates.BeforeWrite;
        fixture.CaptionPreferences.Failure = new IOException("Synthetic caption write failure.");
        var preparation = Task.CompletedTask;
        fixture.CaptionPreferences.AfterSave = () => preparation = fixture.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        var command = Task.Run(() => fixture.RunAsync("set display.speech-text to CurrentUtterance"),
            TestContext.Current.CancellationToken);
        try
        {
            await gates.WaitUntilPreparationAndFailureEntered();
            preparation.IsCompleted.Should().BeFalse();
            fixture.ViewModel.ApprovalPreparation.IsCompleted.Should().BeFalse();
            fixture.ViewModel.IsGrantChangePending.Should().BeFalse();
            fixture.ViewModel.ConfirmGrantChangeCommand.CanExecute(null).Should().BeFalse();
            var title = fixture.ViewModel.ResponseTitle;
            var body = fixture.ViewModel.ResponseBody;
            if (grantCompletesFirst)
            {
                gates.ReleaseGrant();
                await preparation;
                title = fixture.ViewModel.ResponseTitle;
                body = fixture.ViewModel.ResponseBody;
            }
            gates.ReleaseCaptionFailure();
            await command;
            fixture.ViewModel.Transcript.Should().Contain("not confirmed");
            fixture.ViewModel.ResponseTitle.Should().Be(title);
            fixture.ViewModel.ResponseBody.Should().Be(body);
            fixture.ViewModel.SpeechTextConfigurationStatus.Should().Contain("\"available\":false");
            if (!grantCompletesFirst)
            {
                preparation.IsCompleted.Should().BeFalse();
                (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
                gates.ReleaseGrant();
                await preparation;
            }
            fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
            fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
            fixture.ViewModel.ResponseTitle.Should().Be("Confirm exact grant change?");
            fixture.ViewModel.ResponseBody.Should().Contain(nameof(BuiltInAction.LockMachine));
            fixture.ViewModel.Transcript.Should().Contain("not confirmed");
            fixture.ViewModel.AlwaysAllowedModelActions.Should().BeEmpty();
            fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
            fixture.Session.LockCalls.Should().Be(0);
            fixture.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.speech-text", StringComparison.Ordinal))
                .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        }
        finally
        {
            gates.ReleaseAll();
            await Task.WhenAll(command, preparation);
        }
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("privacy")]
    [InlineData("lock")]
    [InlineData("owner")]
    [InlineData("revocation")]
    [InlineData("superseded")]
    public async Task CaptionFailureCannotReviveRetiredPreparationOrOverwriteAnotherGrantReview(string retirement)
    {
        using var gates = new CaptionFailureAuditGates();
        var fixture = new Fixture(enableSpeechText: true, logger: gates);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        var ownerRevision = 0L;
        fixture.ViewModel.BindCallOwnershipGate(static () => true, () => ownerRevision);
        fixture.Audit.BeforeWrite = gates.BeforeWrite;
        fixture.CaptionPreferences.Failure = new IOException("Synthetic caption write failure.");
        var preparation = Task.CompletedTask;
        fixture.CaptionPreferences.AfterSave = () => preparation = fixture.ViewModel.PrepareGrantChangeAsync(
            new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        var command = Task.Run(() => fixture.RunAsync("set display.speech-text to CurrentUtterance"),
            TestContext.Current.CancellationToken);
        try
        {
            await gates.WaitUntilPreparationAndFailureEntered();
            switch (retirement)
            {
                case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
                case "dispose": fixture.ViewModel.Dispose(); break;
                case "privacy":
                    fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 123 };
                    break;
                case "lock": fixture.Session.IsUnlocked = false; break;
                case "owner": ownerRevision++; break;
                case "revocation": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "superseded":
                    await fixture.ViewModel.PrepareGrantChangeAsync(
                        new(GrantChangeOperation.Add, BuiltInAction.ProposeRestart, ModelApprovalScope.Session));
                    break;
            }
            var title = fixture.ViewModel.ResponseTitle;
            var body = fixture.ViewModel.ResponseBody;
            var transcript = fixture.ViewModel.Transcript;
            gates.ReleaseCaptionFailure();
            await command;
            fixture.ViewModel.ResponseTitle.Should().Be(title);
            fixture.ViewModel.ResponseBody.Should().Be(body);
            if (retirement is "dispose" or "privacy" or "lock" or "owner" or "revocation")
            {
                fixture.ViewModel.Transcript.Should().Be(transcript);
            }
            else { fixture.ViewModel.Transcript.Should().Contain("not confirmed"); }
            gates.ReleaseGrant();
            await preparation;
            fixture.ViewModel.IsGrantChangePending.Should().Be(retirement is "superseded");
            if (retirement is "superseded")
            {
                fixture.ViewModel.ResponseTitle.Should().Be(title);
                fixture.ViewModel.ResponseBody.Should().Be(body);
                fixture.ViewModel.ResponseBody.Should().Contain(nameof(BuiltInAction.ProposeRestart));
            }
            fixture.ViewModel.AlwaysAllowedModelActions.Should().BeEmpty();
            fixture.ViewModel.SessionAllowedModelActions.Should().BeEmpty();
            fixture.Session.LockCalls.Should().Be(0);
            fixture.ViewModel.SpeechCaptionText.Should().BeNull();
            fixture.TextToSpeech.SpokenText.Should().BeNull();
            fixture.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded
                && item.Category == SecurityAuditCategory.SecurityApproval);
        }
        finally
        {
            gates.ReleaseAll();
            await Task.WhenAll(command, preparation);
        }
    }

    [Fact]
    public async Task CancelledCaptionControlReportsUnconfirmedWithoutAwaitingTheRequiredGrantAudit()
    {
        using var gates = new CaptionFailureAuditGates();
        var fixture = new Fixture(enableSpeechText: true, logger: gates);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Audit.BeforeWrite = gates.BeforeWrite;
        fixture.CaptionPreferences.Failure = new IOException("Synthetic caption write failure.");
        var preparation = Task.CompletedTask;
        fixture.CaptionPreferences.AfterSave = () =>
        {
            preparation = fixture.ViewModel.PrepareGrantChangeAsync(
                new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
            cancellation.Cancel();
        };
        var command = Task.Run(() => fixture.ViewModel.ExecuteSpeechTextCommandAsync(
            new(AppearanceCommandOperation.Set, SpeechTextMode.CurrentUtterance),
            SecurityAuditInitiator.TypedCommand, cancellationToken: cancellation.Token),
            TestContext.Current.CancellationToken);
        try
        {
            await gates.WaitUntilPreparationAndFailureEntered();
            gates.ReleaseCaptionFailure();
            await command;
            fixture.ViewModel.Transcript.Should().Contain("not confirmed");
            preparation.IsCompleted.Should().BeFalse();
            gates.ReleaseGrant();
            await preparation;
            fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
            fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
            fixture.Session.LockCalls.Should().Be(0);
        }
        finally
        {
            gates.ReleaseAll();
            await Task.WhenAll(command, preparation);
        }
    }

    private sealed class CaptionFailureAuditGates : IDisposable, ILogger<MainViewModel>
    {
        private readonly ManualResetEventSlim grantRelease = new();
        private readonly ManualResetEventSlim captionRelease = new();
        private readonly TaskCompletionSource grantEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource captionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int requestedGrants;

        public void BeforeWrite(SecurityAuditEvent audit)
        {
            if (audit.Category == SecurityAuditCategory.SecurityApproval
                && audit.Outcome == SecurityAuditOutcome.Requested
                && Interlocked.Increment(ref requestedGrants) == 1)
            {
                grantEntered.SetResult();
                grantRelease.Wait(TestContext.Current.CancellationToken);
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Pause outside the preference store's lock, after its failed durable receipt.
            if (logLevel == LogLevel.Error)
            {
                captionEntered.SetResult();
                captionRelease.Wait(TestContext.Current.CancellationToken);
            }
        }

        public Task WaitUntilPreparationAndFailureEntered() =>
            Task.WhenAll(grantEntered.Task, captionEntered.Task).WaitAsync(TestContext.Current.CancellationToken);

        public void ReleaseGrant() => grantRelease.Set();
        public void ReleaseCaptionFailure() => captionRelease.Set();
        public void ReleaseAll() { ReleaseGrant(); ReleaseCaptionFailure(); }
        public void Dispose() { grantRelease.Dispose(); captionRelease.Dispose(); }
    }
}
