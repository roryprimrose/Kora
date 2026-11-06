using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, "Offer")]
    [InlineData(true, "Offer")]
    [InlineData(false, "LinkOnly")]
    [InlineData(true, "LinkOnly")]
    [InlineData(false, "OpenAutomatically")]
    [InlineData(true, "OpenAutomatically")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public async Task DetailsRetainedAnswerRequiresNativeReviewAndSeparateAuditedAtomicSave(bool compact, string? value)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            var preferences = new LocalDetailPresentationPreferences(f.Paths);
            preferences.Save(DetailPresentationMode.LinkOnly);
            var audit = new AuditLog();
            var details = new DetailPresentationConfigurationService(preferences, admission, audit, f.Time);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            var adapter = new SummaryAnswerAdapter(value is null ? "reset responses.detail-presentation" : "set responses.detail-presentation to " + value);
            await using var composed = new Composition(f, adapter: adapter);
            var model = composed.Model;
            model.BindDetailPresentationSuggestions((message, eligible, validate, token) =>
                DetailPresentationSuggestionReview.ReviewAsync(details, policy, f.Time, message, eligible, validate, token),
                (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest how future detailed answers should be presented.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            await ActivateOriginalRequestWindow(window);
            try
            {
                var button = window.GetVisualDescendants().OfType<Button>()
                    .Single(item => string.Equals(item.Name, "ReviewRegisteredSettingSuggestion", StringComparison.Ordinal));
                button.DataContext.Should().BeSameAs(answer);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.RegisteredSettingSuggestionDescription.Should().Contain("LinkOnly -> " + (value ?? "Offer"))
                    .And.Contain("shared device-local").And.Contain("future publications").And.Contain("Confirm separately");
                model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus);
                preferences.Load().Should().Be(DetailPresentationMode.LinkOnly);
                audit.Events.Should().BeEmpty();
                window.FindControl<Button>("ConfirmRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.RegisteredSettingOperation;
                model.ConversationStatus.Should().Contain("saved with committed preference and audit readback");
                new LocalDetailPresentationPreferences(f.Paths).Load().Should().Be(DetailPresentationPreference.Parse(value) ?? DetailPresentationMode.Offer);
                audit.Events.Select(item => (item.ActionId, item.Initiator, item.Outcome)).Should().Equal(
                    ("configuration.detail-presentation", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Requested),
                    ("configuration.detail-presentation", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded));
                model.RegisteredSettingSuggestionDescription.Should().BeEmpty();
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                adapter.Calls.Should().Be(1);
                (await f.Store.ReadConversationMessageAsync(answer.Request.SessionId, answer.Request.RequestId, f.Token)).Should().Be(answer);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                model.ComposerBody.Should().BeEmpty();
                window.IsActive.Should().BeTrue();
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "refresh")]
    [InlineData(true, "refresh")]
    [InlineData(false, "source")]
    [InlineData(true, "source")]
    [InlineData(false, "private")]
    [InlineData(true, "private")]
    [InlineData(false, "control")]
    [InlineData(true, "control")]
    [InlineData(false, "close")]
    [InlineData(true, "close")]
    [InlineData(false, "settings")]
    [InlineData(true, "settings")]
    [InlineData(false, "revision")]
    [InlineData(true, "revision")]
    [InlineData(false, "hide-reopen")]
    [InlineData(true, "hide-reopen")]
    [InlineData(false, "write-unconfirmed")]
    [InlineData(true, "write-unconfirmed")]
    [InlineData(false, "cancel-await")]
    [InlineData(true, "cancel-await")]
    [InlineData(false, "cancel-confirm-await")]
    [InlineData(true, "cancel-confirm-await")]
    [InlineData(false, "voice-laundering")]
    [InlineData(true, "voice-laundering")]
    public async Task DetailsSuggestionRejectsCopiedReplacedAndRevokedNativeSourcesWithoutSuccessfulWrite(bool compact, string transition)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            var preferences = new LocalDetailPresentationPreferences(f.Paths);
            var audit = new AuditLog();
            var details = new DetailPresentationConfigurationService(preferences, admission, audit, f.Time);
            using var policy = new CallCommunicationPolicy(new SummaryCallState());
            await using var composed = new Composition(f, adapter: new SummaryAnswerAdapter("set responses.detail-presentation to OpenAutomatically"));
            var model = composed.Model;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reads = 0;
            model.BindDetailPresentationSuggestions(async (message, eligible, validate, token) =>
            {
                async Task Validate(CancellationToken cancellation)
                {
                    if (transition is "cancel-await" || transition is "cancel-confirm-await" && ++reads == 3)
                    { entered.TrySetResult(); await release.Task; }
                    await validate(cancellation);
                }
                return await DetailPresentationSuggestionReview.ReviewAsync(details, policy, f.Time, message, eligible, Validate, token);
            }, (review, token) => review.ConfirmAsync(token));
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            await model.ReadConversationAsync();
            model.ComposerBody = "Suggest future detail presentation.";
            await model.SubmitConversationAsync();
            var answer = model.ConversationMessages.Single();
            var owner = new SessionsPresentationOwner(model);
            Window window = compact ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            await ActivateOriginalRequestWindow(window);
            try
            {
                await model.ReviewRegisteredSettingSuggestionAsync(answer with { });
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
                if (transition is "voice-laundering")
                {
                    using var voice = HostActivity.BeginRoot(new(new(Guid.NewGuid()), answer.Request.SessionId,
                        new(Guid.NewGuid()), RequestOrigin.ActivatedVoice), HostActivityLayer.Desktop, HostOperation.Request);
                    await model.ReviewRegisteredSettingSuggestionAsync(answer);
                }
                else
                {
                    var work = model.ReviewRegisteredSettingSuggestionAsync(answer);
                    if (transition is "cancel-confirm-await")
                    { await work; work = model.ConfirmRegisteredSettingSuggestionAsync(); }
                    if (transition is "cancel-await" or "cancel-confirm-await")
                    {
                        await Task.WhenAny(entered.Task, work);
                        entered.Task.IsCompleted.Should().BeTrue(model.ConversationStatus);
                        model.CanCancelRegisteredSettingSuggestion.Should().BeTrue();
                        window.FindControl<Button>("CancelRegisteredSettingSuggestion")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        release.TrySetResult();
                    }
                    await work;
                }
                if (transition is not ("cancel-await" or "cancel-confirm-await" or "voice-laundering"))
                { model.CanConfirmRegisteredSettingSuggestion.Should().BeTrue(model.ConversationStatus); }
                if (transition is "cancel") { model.CancelRegisteredSettingSuggestion(); }
                if (transition is "refresh") { await model.ReadConversationAsync(); }
                if (transition is "private") { composed.Access.CanInspect = false; }
                if (transition is "control") { composed.Access.ControlRevision++; }
                if (transition is "close") { model.Close(); }
                if (transition is "settings") { preferences.Save(DetailPresentationMode.LinkOnly); }
                if (transition is "revision") { details.HoldUnavailable(); details.Observe(); }
                if (transition is "hide-reopen") { window.Hide(); window.Show(); }
                if (transition is "write-unconfirmed") { preferences.BeginWrite(); }
                if (transition is "source")
                {
                    var request = new HostRequest(new(Guid.NewGuid()), answer.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
                    using var input = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
                    var preview = await f.Store.PreviewDispositionAsync(answer.Request.SessionId, answer.Generation, 0, f.Token);
                    await f.Tasks.CommitAsync(new(request, new(1), HostTaskState.IntentRecorded), 0, f.Token);
                    await f.Store.DisposeSessionAsync(request, preview, () => true, f.Token);
                }
                await model.ConfirmRegisteredSettingSuggestionAsync();
                preferences.ReadBack().Should().Be(transition is "settings" ? DetailPresentationMode.LinkOnly : null);
                audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
                model.CanConfirmRegisteredSettingSuggestion.Should().BeFalse();
            }
            finally { release.TrySetResult(); owner.Revoke(); }
        });
    }
}
