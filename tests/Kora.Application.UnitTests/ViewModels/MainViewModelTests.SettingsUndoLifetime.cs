using AwesomeAssertions;

using Kora.Application.Configuration.RegisteredSettings;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsUndoOwnsSynchronousWriterBeforeReentryOrDisposal(bool dispose)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RunAsync("set speech.summary-words to 40");
        Task Native(RegisteredSettingSuggestionOperation operation) => HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            () => model.ExecuteSettingsUndoAsync(operation, SecurityAuditInitiator.LocalUser,
                TestContext.Current.CancellationToken, static () => true));
        await Native(RegisteredSettingSuggestionOperation.Review);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var closing = Task.CompletedTask;
        var reentrant = Task.CompletedTask;
        f.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Requested) { return; }
            f.Audit.BeforeWrite = null;
            if (dispose) { closing = model.DisposeAsync().AsTask(); }
            else { reentrant = Native(RegisteredSettingSuggestionOperation.Review); }
            entered.TrySetResult();
            release.Wait(TestContext.Current.CancellationToken);
        };
        var pending = Task.Run(() => Native(RegisteredSettingSuggestionOperation.Confirm),
            TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            if (dispose) { closing.IsCompleted.Should().BeFalse("the original synchronous writer has not returned"); }
            else
            {
                await FluentActions.Awaiting(() => reentrant).Should().ThrowAsync<InvalidOperationException>();
                model.CanReviewSettingsUndo.Should().BeFalse();
                model.CanConfirmSettingsUndo.Should().BeFalse();
            }
        }
        finally { release.Set(); }
        await pending;
        await closing;
        f.Preferences.SummaryLimits.Should().Be(dispose ? new SpokenSummaryLimits(3, 40) : null);
        f.Audit.Events.Should().Contain(item => item.Outcome ==
            (dispose ? SecurityAuditOutcome.Denied : SecurityAuditOutcome.Succeeded));
        model.CanConfirmSettingsUndo.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("native")]
    [InlineData("privacy")]
    [InlineData("disposed")]
    public async Task SettingsUndoTerminalFailureCannotReplaceRetiredPrivatePresentation(string loss)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RunAsync("set speech.summary-words to 40");
        var live = true;
        Task Native(RegisteredSettingSuggestionOperation operation) => HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            () => model.ExecuteSettingsUndoAsync(operation, SecurityAuditInitiator.LocalUser,
                TestContext.Current.CancellationToken, () => live));
        await Native(RegisteredSettingSuggestionOperation.Review);
        var title = model.ResponseTitle;
        var body = model.ResponseBody;
        f.Audit.BeforeWrite = audit =>
        {
            if (audit.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            f.Audit.BeforeWrite = null;
            switch (loss)
            {
                case "owner": model.BindCallOwnershipGate(static () => false); break;
                case "native": live = false; break;
                case "privacy": model.CloseForObservedPrivacyEvent("fixture", true); break;
                case "disposed": model.Dispose(); break;
            }
            title = model.ResponseTitle;
            body = model.ResponseBody;
            throw new IOException("fixture late terminal audit failure");
        };
        await Native(RegisteredSettingSuggestionOperation.Confirm);
        await model.PrivacyClosureTask;
        f.Preferences.SummaryLimits.Should().BeNull("the write preceded terminal audit failure; this is not rollback");
        model.ResponseTitle.Should().Be(title);
        model.ResponseBody.Should().Be(body);
        model.CanConfirmSettingsUndo.Should().BeFalse();
        model.CanReviewSettingsUndo.Should().BeFalse();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
