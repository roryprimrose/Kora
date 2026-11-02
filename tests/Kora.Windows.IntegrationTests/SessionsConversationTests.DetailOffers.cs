using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("ordinary")]
    [InlineData("original-typed")]
    [InlineData("original-voice")]
    public async Task EveryCompletedSessionPublicationCapturesCurrentOfferEpochWithoutRevivingPreviousAnswers(string route)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var preferences = new DetailPresentationSettingsTests.Preferences();
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            using var policy = new CallCommunicationPolicy(new DetailCall());
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), f.Time);
            settings.Observe();
            var adapter = new DetailAnswerAdapter();
            var opened = new List<AdmittedDetailContent>();
            await using var composition = new Composition(f, (content, live, _, _) =>
            {
                live().Should().BeTrue();
                opened.Add(content);
                return Task.FromResult("Exact assistant Details opened");
            }, adapter: adapter);
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var target = model.SelectedSessionRecord!;
            var commands = new SessionRequestCommandService(composition.Workspace, composition.Conversation, composition.Access, f.Time);
            Kora.Core.Conversation.ConversationSubmissionReceipt? originalReceipt = null;
            async Task Publish(string body)
            {
                if (route is "ordinary")
                {
                    model.ComposerBody = body;
                    await model.SubmitConversationAsync();
                }
                else
                {
                    model.ComposerBody = "Independent unsent input";
                    var origin = route is "original-voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
                    composition.Audit.ExpectedOrigin = origin;
                    var review = await commands.ExecuteAsync(new(SessionCommandOperation.RequestReview,
                        target.Authority.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
                        { RequestBody = body }, origin, static () => true, f.Token);
                    var completed = await commands.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
                        target.Authority.SessionId.Value) { RequestReviewId = review.RequestReview!.Id },
                        origin, static () => true, f.Token);
                    originalReceipt = completed.RequestSubmission!;
                    model.PresentOriginalRequestSubmission(originalReceipt);
                    model.ComposerBody.Should().Be("Independent unsent input");
                    originalReceipt.Message.Request.Origin.Should().Be(origin);
                }
                Dispatcher.UIThread.RunJobs();
                model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be(DetailAnswerAdapter.Body);
            }
            async Task Choose(DetailPresentationMode mode)
            {
                var review = await settings.ReviewAsync(mode, RequestOrigin.LocalUi, policy.Current.Revision,
                    policy, static () => true, f.Token);
                (await settings.ConfirmAsync(review, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
                    policy, static () => true, f.Token)).Should().BeTrue();
                Dispatcher.UIThread.RunJobs();
            }
            await Publish("First detailed answer");
            var first = model.ConversationMessages.Single();
            model.DetailsOfferMessage.Should().BeSameAs(first, "unsaved legacy Offer must apply to ordinary and foreground completion");
            await Choose(DetailPresentationMode.LinkOnly);
            model.DetailsOfferMessage.Should().BeNull();
            await Choose(DetailPresentationMode.Offer);
            model.DetailsOfferMessage.Should().BeNull("reset never grants a fresh epoch to old content");
            if (originalReceipt is not null)
            {
                model.PresentOriginalRequestSubmission(originalReceipt);
                model.DetailsOfferMessage.Should().BeNull("a duplicate completion cannot refresh the same immutable answer's epoch");
            }
            await Publish("New answer after reviewed reset");
            model.DetailsOfferMessage.Should().BeSameAs(model.ConversationMessages.Single());
            adapter.Calls.Should().Be(2);
            opened.Should().BeEmpty("no completed publication opens Details automatically");
            await model.OpenConversationDetailAsync(model.ConversationMessages.Single(), assistant: true);
            opened.Should().ContainSingle().Which.Source.Should().Be(DetailAnswerAdapter.Body);
            preferences.Writes.Should().Be(2);
            (await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token)).Records.Should().BeEmpty();
            f.Count("scoped_grants").Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalPublicationBeforeDelayedAcceptancePreservesCompletedStatusAndExactManualDetails(bool linkOnly)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = provider.Task };
            var opened = new List<AdmittedDetailContent>();
            await using var composition = new Composition(f, (content, live, _, _) =>
            {
                live().Should().BeTrue();
                opened.Add(content);
                return Task.FromResult("Exact assistant Details opened");
            }, adapter: adapter);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            var preferences = new DetailPresentationSettingsTests.Preferences
            { Value = linkOnly ? DetailPresentationMode.LinkOnly : null };
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), f.Time);
            settings.Observe();
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var terminal = new TaskCompletionSource<ConversationSubmissionReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            composition.Conversation.SubmittedConversationCompleted += (_, completion) =>
            {
                if (completion.Receipt is { } receipt) { terminal.TrySetResult(receipt); }
            };
            var acceptance = new DetailPublicationJoinContext();
            model.ComposerBody = "Terminal answer before its original acceptance callback";
            var previous = SynchronizationContext.Current;
            Task pending;
            try
            {
                SynchronizationContext.SetSynchronizationContext(acceptance);
                pending = model.SubmitConversationAsync();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            try
            {
                await adapter.Entered.Task.WaitAsync(f.Token);
                await acceptance.WaitForPostAsync(f.Token);
                provider.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(DetailAnswerAdapter.Body, null)));
                var receipt = await terminal.Task.WaitAsync(f.Token);
                Dispatcher.UIThread.RunJobs();
                model.ConversationMessages.Single().Should().BeSameAs(receipt.Message);
                var completedStatus = model.ConversationStatus;
                completedStatus.Should().Contain("HOST Succeeded").And.Contain("Genuine ASSISTANT answer retained");
                pending.IsCompleted.Should().BeFalse();
                var auditBeforeAcceptance = WindowsSqliteConversationTests.AuditText(f);
                acceptance.RunNext();
                await pending;
                model.ConversationStatus.Should().Be(completedStatus,
                    "delayed pending acceptance and the duplicate terminal join cannot replace an already published terminal status");
                model.ConversationMessages.Single().Should().BeSameAs(receipt.Message);
                model.DetailsOfferMessage.Should().Be(linkOnly ? null : receipt.Message);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(auditBeforeAcceptance);
                opened.Should().BeEmpty();
                await WaitForDetailReadinessAsync(model, f.Token);
                await model.OpenConversationDetailAsync(receipt.Message, assistant: true);
                opened.Should().ContainSingle().Which.Source.Should().Be(DetailAnswerAdapter.Body);
                adapter.Calls.Should().Be(1);
                preferences.Writes.Should().Be(0);
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                provider.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(DetailAnswerAdapter.Body, null)));
                while (!pending.IsCompleted)
                {
                    await acceptance.WaitForPostAsync(f.Token);
                    acceptance.RunNext();
                }
                await pending;
            }
        });
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task SharedDetailPreferenceSuppressesFutureSessionOffersButNeverManualAccess(int surface, bool linkOnly)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var preferences = new DetailPresentationSettingsTests.Preferences
            { Value = linkOnly ? DetailPresentationMode.LinkOnly : null };
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            var audit = new AuditLog();
            using var policy = new CallCommunicationPolicy(new DetailCall());
            var settings = new DetailPresentationConfigurationService(preferences, admission, audit, f.Time);
            settings.Observe();
            var adapter = new DetailAnswerAdapter();
            var opened = new List<AdmittedDetailContent>();
            await using var composed = new Composition(f, (content, live, _, _) =>
            {
                live().Should().BeTrue();
                opened.Add(content);
                return Task.FromResult("Exact assistant Details opened");
            }, adapter: adapter);
            var model = composed.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var owner = new SessionsPresentationOwner(model);
            var interaction = new SessionInteractionView { DataContext = model };
            Window window = surface == 2 ? new Window { Content = interaction, Width = 1200, Height = 1000 }
                : surface == 1 ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                model.ComposerBody = "Prepare the detailed plan";
                await model.SubmitConversationAsync();
                var source = model.ConversationMessages.Single();
                model.DetailsOfferMessage.Should().Be(linkOnly ? null : source);
                opened.Should().BeEmpty("publication cannot open anything automatically");
                window.UpdateLayout();
                if (surface == 2)
                {
                    var offers = interaction.GetVisualDescendants().OfType<Button>()
                        .Where(button => string.Equals(button.Name, "DeclineSessionDetailsOffer", StringComparison.Ordinal) && button.IsEffectivelyVisible);
                    offers.Should().HaveCount(linkOnly ? 0 : 1);
                    interaction.GetVisualDescendants().OfType<Button>()
                        .Single(button => string.Equals(button.Name, "OpenAssistantConversationDetail", StringComparison.Ordinal) && Equals(button.Content, "Open full detailed answer"))
                        .IsEnabled.Should().BeTrue();
                }
                await model.OpenConversationDetailAsync(source, assistant: true);
                opened.Should().ContainSingle().Which.Source.Should().Be(DetailAnswerAdapter.Body);
                if (!linkOnly) { model.DismissDetailsOffer(source); }
                preferences.Writes.Should().Be(0, "one-off open/decline cannot choose a lasting preference");
                async Task Choose(DetailPresentationMode mode)
                {
                    var candidate = await settings.ReviewAsync(mode, RequestOrigin.LocalUi, policy.Current.Revision,
                        policy, static () => true, f.Token);
                    (await settings.ConfirmAsync(candidate, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
                        policy, static () => true, f.Token)).Should().BeTrue();
                    Dispatcher.UIThread.RunJobs();
                }
                await Choose(DetailPresentationMode.LinkOnly);
                await Choose(DetailPresentationMode.Offer);
                model.DetailsOfferMessage.Should().BeNull("switching back cannot resurrect the last completion");
                opened.Should().ContainSingle();
                adapter.Calls.Should().Be(1);
                model.ComposerBody = "Prepare another detailed plan";
                await model.SubmitConversationAsync();
                model.DetailsOfferMessage.Should().BeSameAs(model.ConversationMessages.Single());
                await WaitForDetailReadinessAsync(model, f.Token);
                Dispatcher.UIThread.RunJobs();
                composed.Access.CanInspect = composed.Access.CanControl = false;
                composed.Access.ControlRevision++;
                model.DetailsOfferMessage.Should().BeNull();
                await model.OpenConversationDetailAsync(source, assistant: true);
                opened.Should().ContainSingle("privacy and stale-source admission still wins over manual access");
                model.Close();
                var notifications = 0;
                model.PropertyChanged += (_, _) => notifications++;
                settings.HoldUnavailable();
                Dispatcher.UIThread.RunJobs();
                notifications.Should().Be(0, "closed consumers unsubscribe and queued events cannot restore content");
            }
            finally { owner.Revoke(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeferredPassiveRefreshCannotNotifyOrRestoreClosedDetailsConsumer(bool close)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new Composition(f);
            var model = composition.Model;
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "Exact unsent input";
            var source = model.CaptureDraftPresentationEligibility();
            var join = new DetailPublicationJoinContext();
            var previous = SynchronizationContext.Current;
            Task refresh;
            try
            {
                SynchronizationContext.SetSynchronizationContext(join);
                refresh = model.RefreshWorkAsync();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            try
            {
                refresh.IsCompleted.Should().BeFalse();
                await join.WaitForPostAsync(f.Token);
                if (close) { model.Close(); }
                var notifications = 0;
                model.PropertyChanged += (_, _) => notifications++;
                while (!refresh.IsCompleted)
                {
                    await join.WaitForPostAsync(f.Token);
                    join.RunNext();
                }
                await refresh;
                Dispatcher.UIThread.RunJobs();
                if (close)
                {
                    notifications.Should().Be(0, "a joined passive observation cannot republish a closed consumer");
                    source().Should().BeFalse();
                    model.SelectedSessionRecord.Should().BeNull();
                    model.ConversationMessages.Should().BeEmpty();
                    model.DetailsOfferMessage.Should().BeNull();
                    model.ComposerBody.Should().BeEmpty();
                }
                else
                {
                    notifications.Should().BeGreaterThan(0);
                    source().Should().BeTrue();
                    model.SelectedSessionRecord.Should().NotBeNull();
                    model.ComposerBody.Should().Be("Exact unsent input");
                }
                f.Count("conversation_messages").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                while (!refresh.IsCompleted)
                {
                    await join.WaitForPostAsync(f.Token);
                    join.RunNext();
                }
                await refresh;
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldProviderCompletionUsesCurrentConfirmedPreferenceWithoutRedispatchOrQuestionAuthority(bool restoreOffer)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var release = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = release.Task };
            var opened = new List<AdmittedDetailContent>();
            await using var composition = new Composition(f, (content, live, _, _) =>
            {
                live().Should().BeTrue();
                opened.Add(content);
                return Task.FromResult("Exact assistant Details opened");
            }, adapter: adapter);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            using var policy = new CallCommunicationPolicy(new DetailCall());
            var preferences = new DetailPresentationSettingsTests.Preferences();
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), f.Time);
            settings.Observe();
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            model.PropertyChanged += (_, _) =>
            {
                if (model.ConversationMessages.Count == 1 && model.ConversationMessages[0].AssistantResponse is null)
                { accepted.TrySetResult(); }
            };
            model.ComposerBody = "Wait for the exact provider result";
            var pending = model.SubmitConversationAsync();
            try
            {
                await accepted.Task.WaitAsync(f.Token);
                await adapter.Entered.Task.WaitAsync(f.Token);
                var review = await settings.ReviewAsync(DetailPresentationMode.LinkOnly, RequestOrigin.LocalUi,
                    policy.Current.Revision, policy, static () => true, f.Token);
                (await settings.ConfirmAsync(review, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
                    policy, static () => true, f.Token)).Should().BeTrue();
                Dispatcher.UIThread.RunJobs();
                if (restoreOffer)
                {
                    var reset = await settings.ReviewAsync(DetailPresentationMode.Offer, RequestOrigin.LocalUi,
                        policy.Current.Revision, policy, static () => true, f.Token);
                    (await settings.ConfirmAsync(reset, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
                        policy, static () => true, f.Token)).Should().BeTrue();
                    Dispatcher.UIThread.RunJobs();
                }
            }
            finally
            {
                release.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new(DetailAnswerAdapter.Body, null)));
                await pending;
            }
            model.DetailsOfferMessage.Should().Be(restoreOffer ? model.ConversationMessages.Single() : null,
                "a new finalized answer captures publication-time policy, not the earlier pending request's retired cue");
            model.ConversationMessages.Single().AssistantResponse!.Body.Should().Be(DetailAnswerAdapter.Body);
            adapter.Calls.Should().Be(1);
            await WaitForDetailReadinessAsync(model, f.Token);
            model.CanOpenConversationDetails.Should().BeTrue();
            await model.OpenConversationDetailAsync(model.ConversationMessages.Single(), assistant: true);
            opened.Should().ContainSingle().Which.Source.Should().Be(DetailAnswerAdapter.Body);
            (await f.Store.ReadQuestionPageAsync(f.Request.SessionId, null, 50, f.Token)).Records.Should().BeEmpty();
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TerminalEventThenLateJoinAndForegroundDuplicateCannotReplayOfferOrReplaceObservedContent(bool reset, bool replace)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var release = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = release.Task };
            var opened = new List<AdmittedDetailContent>();
            await using var composition = new Composition(f, (content, live, _, _) =>
            {
                live().Should().BeTrue();
                opened.Add(content);
                return Task.FromResult("Exact assistant Details opened");
            }, adapter: adapter);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            using var policy = new CallCommunicationPolicy(new DetailCall());
            var preferences = new DetailPresentationSettingsTests.Preferences();
            var settingsAudit = new AuditLog();
            var settings = new DetailPresentationConfigurationService(preferences, admission, settingsAudit, f.Time);
            settings.Observe();
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var target = model.SelectedSessionRecord!;
            var terminal = new TaskCompletionSource<ConversationSubmissionReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            composition.Conversation.SubmittedConversationCompleted += (_, completion) =>
            {
                if (completion.Receipt is { } receipt) { terminal.TrySetResult(receipt); }
            };
            var join = new DetailPublicationJoinContext();
            model.ComposerBody = "First bounded detailed answer";
            var previous = SynchronizationContext.Current;
            Task pending;
            try
            {
                SynchronizationContext.SetSynchronizationContext(join);
                pending = model.SubmitConversationAsync();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            try
            {
                await adapter.Entered.Task.WaitAsync(f.Token);
                await join.WaitForPostAsync(f.Token);
                join.RunNext();
                model.ConversationMessages.Single().Outcome.Should().BeNull("only acceptance has reached the real submission caller");
                model.DetailsOfferMessage.Should().BeNull();
                release.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(DetailAnswerAdapter.Body, null)));
                var first = await terminal.Task.WaitAsync(f.Token);
                await join.WaitForPostAsync(f.Token);
                Dispatcher.UIThread.RunJobs();
                model.DetailsOfferMessage.Should().BeSameAs(first.Message, "the actual terminal event publishes before the held join");
                pending.IsCompleted.Should().BeFalse();
                if (reset)
                {
                    await ChooseDetailMode(settings, policy, f, DetailPresentationMode.LinkOnly);
                    await ChooseDetailMode(settings, policy, f, DetailPresentationMode.Offer);
                }
                else { model.DismissDetailsOffer(first.Message); }
                await model.ReadConversationAsync();
                var page = model.ConversationMessages;
                page.Should().ContainSingle();
                var auditBeforeDuplicate = WindowsSqliteConversationTests.AuditText(f);
                model.PresentOriginalRequestSubmission(first with { Message = first.Message with { } });
                model.ConversationMessages.Single().Should().BeSameAs(page.Single(),
                    "duplicate identity is independent of object identity and must preserve the page");
                model.DetailsOfferMessage.Should().BeNull();
                WindowsSqliteConversationTests.AuditText(f).Should().Be(auditBeforeDuplicate);
                ConversationMessage? second = null;
                if (replace)
                {
                    var commands = new SessionRequestCommandService(composition.Workspace, composition.Conversation, composition.Access, f.Time);
                    var review = await commands.ExecuteAsync(new(SessionCommandOperation.RequestReview,
                        target.Authority.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
                        { RequestBody = "Another request with the identical detailed answer" }, RequestOrigin.LocalUi, static () => true, f.Token);
                    var completed = await commands.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
                        target.Authority.SessionId.Value) { RequestReviewId = review.RequestReview!.Id },
                        RequestOrigin.LocalUi, static () => true, f.Token);
                    model.PresentOriginalRequestSubmission(completed.RequestSubmission!);
                    second = model.ConversationMessages.Single();
                    second.Request.Should().NotBe(first.Message.Request);
                    second.AssistantResponse!.Body.Should().Be(first.Message.AssistantResponse!.Body);
                    model.DetailsOfferMessage.Should().BeSameAs(second);
                }
                var auditBeforeJoin = WindowsSqliteConversationTests.AuditText(f);
                join.RunNext();
                await pending;
                model.PresentOriginalRequestSubmission(first);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(auditBeforeJoin,
                    "duplicate presentation cannot write retained content, audit or renewed authority");
                if (replace)
                {
                    model.ConversationMessages.Single().Should().BeSameAs(second);
                    model.DetailsOfferMessage.Should().BeSameAs(second, "old A must not replace B or renew its own retired cue");
                }
                else
                {
                    model.ConversationMessages.Single().Should().BeSameAs(page.Single());
                    model.DetailsOfferMessage.Should().BeNull("the late join must not clear an explicitly read page");
                }
                opened.Should().BeEmpty("neither first publication nor duplicates can auto-open Details");
                await model.OpenConversationDetailAsync(model.ConversationMessages.Single(), assistant: true);
                opened.Should().ContainSingle().Which.Source.Should().Be(DetailAnswerAdapter.Body);
                model.ComposerBody = "Genuinely new terminal publication";
                await model.SubmitConversationAsync();
                Dispatcher.UIThread.RunJobs();
                model.DetailsOfferMessage.Should().BeSameAs(model.ConversationMessages.Single());
                model.ConversationMessages.Single().Request.Should().NotBe(first.Message.Request);
                adapter.Calls.Should().Be(replace ? 3 : 2);
                preferences.Writes.Should().Be(reset ? 2 : 0);
                settingsAudit.Events.Count(item => string.Equals(item.ActionId, "configuration.detail-presentation", StringComparison.Ordinal)
                    && item.Outcome == SecurityAuditOutcome.Succeeded).Should().Be(reset ? 2 : 0);
                f.Count("conversation_messages").Should().Be(replace ? 3 : 2);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally
            {
                release.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(DetailAnswerAdapter.Body, null)));
                while (!pending.IsCompleted)
                {
                    await join.WaitForPostAsync(f.Token);
                    join.RunNext();
                }
                await pending;
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualTerminalPresentationHeldAfterJoinCannotReplaceNewPublicationAfterReadAndRetirement(bool reset)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = provider.Task };
            await using var composition = new Composition(f, adapter: adapter);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            using var policy = new CallCommunicationPolicy(new DetailCall());
            var preferences = new DetailPresentationSettingsTests.Preferences();
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), f.Time);
            settings.Observe();
            var arrived = new TaskCompletionSource<ConversationSubmissionReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            var claimed = 0;
            composition.Conversation.SubmittedConversationCompleted += (_, completion) =>
            {
                if (completion.Receipt is not { } receipt || Interlocked.CompareExchange(ref claimed, 1, 0) != 0) { return; }
                arrived.SetResult(receipt);
            };
            var dispatcher = new DeferredConversationDispatcher();
            var model = CreateDeferredPresentation(f, composition, dispatcher);
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            model.ComposerBody = "Join publishes before deferred terminal presentation";
            var pending = model.SubmitConversationAsync();
            try
            {
                await adapter.Entered.Task.WaitAsync(f.Token);
                provider.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(DetailAnswerAdapter.Body, null)));
                var first = await arrived.Task.WaitAsync(f.Token);
                await pending;
                dispatcher.Count.Should().BeGreaterThan(0, "terminal observers must join while their native presentation remains deferred");
                model.DetailsOfferMessage.Should().BeSameAs(first.Message);
                if (reset)
                {
                    await ChooseDetailMode(settings, policy, f, DetailPresentationMode.LinkOnly);
                    await ChooseDetailMode(settings, policy, f, DetailPresentationMode.Offer);
                }
                else { model.DismissDetailsOffer(first.Message); }
                await model.ReadConversationAsync();
                model.ComposerBody = "New request while old presentation waits";
                await model.SubmitConversationAsync();
                var second = model.ConversationMessages.Single();
                second.Request.Should().NotBe(first.Message.Request);
                model.DetailsOfferMessage.Should().BeSameAs(second);
                dispatcher.Drain();
                Dispatcher.UIThread.RunJobs();
                model.ConversationMessages.Single().Should().BeSameAs(second);
                model.DetailsOfferMessage.Should().BeSameAs(second, "the real late terminal presentation must not recapture A's policy or replace B");
                preferences.Writes.Should().Be(reset ? 2 : 0);
                adapter.Calls.Should().Be(2);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally
            {
                provider.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(DetailAnswerAdapter.Body, null)));
                await pending;
                dispatcher.Drain();
                model.Close();
            }
        });
    }

    [Fact]
    public async Task PreviouslyPendingOlderRequestCapturesPolicyAtItsFirstTerminalJoinEvenAfterNewerTerminalPublication()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var adapter = new MemoryAnswerAdapter { Pending = provider.Task };
            await using var composition = new Composition(f, adapter: adapter);
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            using var policy = new CallCommunicationPolicy(new DetailCall());
            var preferences = new DetailPresentationSettingsTests.Preferences();
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), f.Time);
            settings.Observe();
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single());
            var target = model.SelectedSessionRecord!;
            var terminal = new TaskCompletionSource<ConversationSubmissionReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            composition.Conversation.SubmittedConversationCompleted += (_, completion) =>
            {
                if (completion.Receipt is { } receipt) { terminal.TrySetResult(receipt); }
            };
            var join = new DetailPublicationJoinContext();
            model.ComposerBody = "Pending A, not yet a terminal publication";
            var previous = SynchronizationContext.Current;
            Task pending;
            try
            {
                SynchronizationContext.SetSynchronizationContext(join);
                pending = model.SubmitConversationAsync();
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
            try
            {
                await adapter.Entered.Task.WaitAsync(f.Token);
                await join.WaitForPostAsync(f.Token);
                join.RunNext();
                model.ConversationMessages.Single().Outcome.Should().BeNull();
                await model.ReadConversationAsync();
                await ChooseDetailMode(settings, policy, f, DetailPresentationMode.LinkOnly);
                provider.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(DetailAnswerAdapter.Body, null)));
                var first = await terminal.Task.WaitAsync(f.Token);
                await join.WaitForPostAsync(f.Token);
                Dispatcher.UIThread.RunJobs();
                model.ConversationMessages.Single().Outcome.Should().BeNull("the read page fences A's terminal event while its join is still held");
                await ChooseDetailMode(settings, policy, f, DetailPresentationMode.Offer);
                var commands = new SessionRequestCommandService(composition.Workspace, composition.Conversation, composition.Access, f.Time);
                var review = await commands.ExecuteAsync(new(SessionCommandOperation.RequestReview,
                    target.Authority.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
                    { RequestBody = "New B before A's first terminal presentation" }, RequestOrigin.LocalUi, static () => true, f.Token);
                var completed = await commands.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
                    target.Authority.SessionId.Value) { RequestReviewId = review.RequestReview!.Id },
                    RequestOrigin.LocalUi, static () => true, f.Token);
                var second = completed.RequestSubmission!;
                model.PresentOriginalRequestSubmission(second);
                model.DetailsOfferMessage.Should().BeSameAs(second.Message);
                second.Message.Sequence.Should().BeGreaterThan(first.Message.Sequence);
                await ChooseDetailMode(settings, policy, f, DetailPresentationMode.LinkOnly);
                await ChooseDetailMode(settings, policy, f, DetailPresentationMode.Offer);
                model.DetailsOfferMessage.Should().BeNull();
                join.RunNext();
                await pending;
                model.DetailsOfferMessage.Should().BeSameAs(first.Message,
                    "sequence ordering and pending acceptance must not misclassify A's first terminal publication as a duplicate");
                model.PresentOriginalRequestSubmission(second);
                model.DetailsOfferMessage.Should().BeSameAs(first.Message, "B's duplicate must not replace A or recapture its retired epoch");
                model.DismissDetailsOffer(first.Message);
                await model.ReadConversationAsync();
                var page = model.ConversationMessages;
                model.PresentOriginalRequestSubmission(first);
                model.ConversationMessages.Should().Equal(page, (actual, expected) => ReferenceEquals(actual, expected));
                model.DetailsOfferMessage.Should().BeNull();
                preferences.Writes.Should().Be(4);
                adapter.Calls.Should().Be(2);
                f.Count("conversation_messages").Should().Be(2);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally
            {
                provider.TrySetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(DetailAnswerAdapter.Body, null)));
                while (!pending.IsCompleted)
                {
                    await join.WaitForPostAsync(f.Token);
                    join.RunNext();
                }
                await pending;
            }
        });
    }

    [Fact]
    public async Task PublicationRecordsAreScopedToExactPrivateSelectionAndRetiredOnPolicyRebindAndClose()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var original = f.Request.SessionId;
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await using var composition = new Composition(f, adapter: new DetailAnswerAdapter());
            await using var admission = new AudioControlAdmission(f.Store, f.Store, new(f.Tasks));
            var preferences = new DetailPresentationSettingsTests.Preferences();
            var settings = new DetailPresentationConfigurationService(preferences, admission, new AuditLog(), f.Time);
            settings.Observe();
            var model = composition.Model;
            model.BindDetailPresentation(settings);
            await model.RefreshAsync();
            await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId == original));
            var first = await composition.Conversation.SubmitAsync(model.SelectedSessionRecord!, "First session's answer",
                static () => true, f.Token);
            model.PresentOriginalRequestSubmission(first);
            Dispatcher.UIThread.RunJobs();
            model.DetailsOfferMessage.Should().BeSameAs(first.Message);
            await model.SelectAsync(model.Sessions.Single(item => item.Authority.SessionId != original));
            model.ConversationMessages.Should().BeEmpty();
            model.PresentOriginalRequestSubmission(first);
            model.ConversationMessages.Should().BeEmpty("a foreign receipt cannot reuse a sequence slot in the new selection");
            model.ComposerBody = "Second session's answer";
            await model.SubmitConversationAsync();
            var second = model.ConversationMessages.Single();
            second.Sequence.Should().Be(first.Message.Sequence, "each exact session starts its own bounded sequence");
            model.DetailsOfferMessage.Should().BeSameAs(second);
            model.BindDetailPresentation(settings);
            model.DetailsOfferMessage.Should().BeNull("rebinding cannot reinterpret a prior epoch as a new offer");
            model.ComposerBody = "New answer after policy rebind";
            await model.SubmitConversationAsync();
            model.DetailsOfferMessage.Should().BeSameAs(model.ConversationMessages.Single());
            composition.Access.CanInspect = composition.Access.CanControl = false;
            composition.Access.ControlRevision++;
            model.DetailsOfferMessage.Should().BeNull();
            model.Close();
            model.ConversationMessages.Should().BeEmpty();
            var notifications = 0;
            model.PropertyChanged += (_, _) => notifications++;
            model.PresentOriginalRequestSubmission(first);
            settings.HoldUnavailable();
            Dispatcher.UIThread.RunJobs();
            model.DetailsOfferMessage.Should().BeNull();
            model.ConversationMessages.Should().BeEmpty();
            notifications.Should().Be(0);
            preferences.Writes.Should().Be(0);
            f.Count("host_questions").Should().Be(0);
            f.Count("scoped_grants").Should().Be(0);
            f.Count("conversation_drafts").Should().Be(0);
        });
    }

    private static async Task ChooseDetailMode(DetailPresentationConfigurationService settings, CallCommunicationPolicy policy,
        InteractionStorageFixture f, DetailPresentationMode mode)
    {
        var review = await settings.ReviewAsync(mode, RequestOrigin.LocalUi, policy.Current.Revision,
            policy, static () => true, f.Token);
        (await settings.ConfirmAsync(review, RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser,
            policy, static () => true, f.Token)).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitForDetailReadinessAsync(SessionsViewModel model, CancellationToken token)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (model.CanReadConversation) { ready.TrySetResult(); }
        }
        model.PropertyChanged += Changed;
        try
        {
            Changed(model, new(nameof(model.CanReadConversation)));
            await ready.Task.WaitAsync(token);
        }
        finally { model.PropertyChanged -= Changed; }
    }

    private sealed class DetailPublicationJoinContext : SynchronizationContext
    {
        private readonly Lock gate = new();
        private readonly Queue<Action> callbacks = new();
        private TaskCompletionSource posted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (gate)
            {
                callbacks.Enqueue(() => callback(state));
                posted.TrySetResult();
            }
        }
        internal Task WaitForPostAsync(CancellationToken token)
        {
            lock (gate) { return callbacks.Count != 0 ? Task.CompletedTask : posted.Task.WaitAsync(token); }
        }
        internal void RunNext()
        {
            Action callback;
            lock (gate)
            {
                callback = callbacks.Dequeue();
                if (callbacks.Count == 0) { posted = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            }
            var previous = Current;
            try { SetSynchronizationContext(this); callback(); }
            finally { SetSynchronizationContext(previous); }
        }
    }

    private sealed class DetailCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
    private sealed class DetailAnswerAdapter : IModelTurnAdapter
    {
        internal const string Body = "Brief response:\nA bounded summary.\n\nDetailed response:\n# Exact answer\n\n| A | B |\n|---|---|\n| 1 | 2 |";
        internal int Calls { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            Encoding.UTF8.GetString(context.Span).Should().NotContain("responses.detail-presentation");
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(Body, null)));
        }
    }
}
