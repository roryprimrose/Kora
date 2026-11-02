using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(0, true, true)]
    [InlineData(23, true, true)]
    [InlineData(25, false, true)]
    [InlineData(600, false, true)]
    [InlineData(0, true, false)]
    [InlineData(600, false, false)]
    public async Task ConversationReaderFollowsOnlyTheAdmittedTailAndKeepsHistoricalAnchorAcrossActualSubmission(
        double distance, bool follows, bool answer)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await SeedReadingMessages(f, f.Request.SessionId, 8);
            var adapter = new MemoryAnswerAdapter();
            var reader = new SearchReader(f.Store.ReadConversationPageAsync);
            await using var composition = new Composition(f, adapter: answer ? adapter : null, conversationReader: reader);
            var model = composition.Model;
            var interaction = new SessionInteractionView { DataContext = model };
            var window = ReadingWindow(interaction);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                ReadingLayout(window);
                var scroll = interaction.FindControl<ScrollViewer>("ConversationScroll")!;
                scroll.Viewport.Height.Should().BeGreaterThan(100);
                (scroll.Extent.Height - scroll.Viewport.Height).Should().BeGreaterThan(900);
                scroll.Offset = new(0, scroll.Extent.Height - scroll.Viewport.Height - distance);
                ReadingLayout(window);
                var anchor = ReadingAnchor(interaction);
                var original = model.ConversationMessages.ToArray();
                var reads = reader.Reads;
                scroll.Focus().Should().BeTrue();
                model.ComposerBody = "New original request while the earlier page stays admitted.";
                await model.SubmitConversationAsync();
                ReadingLayout(window);
                model.ConversationMessages.Should().Equal(original);
                model.ConversationUpdates.Should().ContainSingle();
                var completion = model.ConversationUpdates.Single();
                completion.Sequence.Should().Be(9);
                completion.HasAssistantResponse.Should().Be(answer);
                completion.Outcome.Should().Be(answer ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Unavailable);
                reader.Reads.Should().Be(reads, "a completion cannot silently read or extend the old snapshot");
                model.HasConversationUpdates.Should().BeTrue();
                var button = interaction.FindControl<Button>("NewConversationUpdates")!;
                button.Content.Should().Be("New updates - read latest");
                button.IsEnabled.Should().BeTrue();
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(scroll);
                if (follows)
                { (scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y).Should().BeLessThan(1); }
                else
                {
                    var kept = ReadingAnchor(interaction);
                    kept.Message.Should().BeSameAs(anchor.Message);
                    kept.Offset.Should().BeApproximately(anchor.Offset, 0.1);
                }
                interaction.GetVisualDescendants().OfType<TextBlock>()
                    .Should().Contain(block => block.Text == completion.Body);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                adapter.Calls.Should().Be(answer ? 1 : 0);
                await model.SelectAsync(null);
                model.HasConversationUpdates.Should().BeFalse("update notices belong only to the exact selected session");
            }
            finally { model.Close(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(Key.Enter, PhysicalKey.Enter, "\r")]
    [InlineData(Key.Space, PhysicalKey.Space, " ")]
    public async Task ConversationReaderNewUpdatesKeyboardReadsFreshBoundedExactSessionPageInContextWithoutWrites(
        Key key, PhysicalKey physical, string symbol)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var originalSession = f.Request.SessionId;
            await SeedReadingMessages(f, originalSession, 26);
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await SeedReadingMessages(f, f.Request.SessionId, 1);
            var sources = new List<HostId<SessionIdentity>>();
            var reader = new SearchReader((session, cursor, limit, token) =>
            {
                sources.Add(session);
                return f.Store.ReadConversationPageAsync(session, cursor, limit, token);
            });
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter, conversationReader: reader);
            var model = composition.Model;
            var interaction = new SessionInteractionView { DataContext = model };
            var window = ReadingWindow(interaction);
            window.Show();
            try
            {
                await model.RefreshAsync();
                var list = model.Sessions.ToArray();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == originalSession));
                await model.ReadConversationAsync();
                ReadingLayout(window);
                var scroll = interaction.FindControl<ScrollViewer>("ConversationScroll")!;
                scroll.Offset = new(0, 650);
                ReadingLayout(window);
                model.ConversationMessages.Should().HaveCount(25);
                model.CanNextConversation.Should().BeTrue();
                model.ComposerBody = "Newest genuine answer belongs to this exact conversation.";
                await model.SubmitConversationAsync();
                model.ConversationMessages.Should().HaveCount(25);
                model.ConversationUpdates.Single().Sequence.Should().Be(27);
                await model.ReadConversationAsync(next: true);
                model.ConversationMessages.Should().ContainSingle().Which.Sequence.Should().Be(26,
                    "the old continuation cannot silently admit the appended answer");
                model.ConversationUpdates.Should().ContainSingle().Which.Sequence.Should().Be(27);
                model.HasConversationUpdates.Should().BeTrue();
                ReadingLayout(window);
                interaction.FindControl<TextBlock>("ConversationReaderStatus")!.Text.Should().Contain("no replacement anchor");
                model.ComposerBody = "  independent unsent input e\u0301\r\n stays exactly  ";
                var unsent = model.ComposerBody;
                var retention = await f.Store.ReadRetentionAsync(originalSession, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var reads = reader.Reads;
                ReadingLayout(window);
                var latest = interaction.FindControl<Button>("NewConversationUpdates")!;
                AutomationProperties.GetName(latest).Should().Contain("exact conversation").And.Contain("keyboard focus");
                latest.Focus().Should().BeTrue();
                window.KeyPress(key, RawInputModifiers.None, physical, symbol);
                window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
                await interaction.Operation;
                ReadingLayout(window);
                reader.Reads.Should().Be(reads + 2);
                sources.Should().OnlyContain(session => session == originalSession);
                model.ConversationMessages.Select(message => message.Sequence).Should().Equal(26, 27);
                model.ConversationMessages[^1].AssistantResponse!.Body.Should().Be("Answer from reviewed session fact.");
                model.ConversationUpdates.Should().BeEmpty();
                model.HasConversationUpdates.Should().BeFalse();
                model.CanNextConversation.Should().BeFalse();
                model.ComposerBody.Should().Be(unsent);
                model.Sessions.Should().Equal(list);
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(latest);
                (scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y).Should().BeLessThan(1);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadRetentionAsync(originalSession, f.Token)).Should().Be(retention);
                reader.DraftReads.Should().Be(0);
                adapter.Calls.Should().Be(1);
                f.Count("conversation_messages").Should().Be(28);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { model.Close(); window.Close(); }
        });
    }

    [Fact]
    public async Task ConversationReaderRestoresOnlyExactSessionAnchorAcrossSwitchResizeAndHide()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var first = f.Request.SessionId;
            await SeedReadingMessages(f, first, 8);
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            await SeedReadingMessages(f, f.Request.SessionId, 8);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync);
            await using var composition = new Composition(f, conversationReader: reader);
            var model = composition.Model;
            var interaction = new SessionInteractionView { DataContext = model };
            var window = ReadingWindow(interaction);
            window.Show();
            try
            {
                await model.RefreshAsync();
                var original = model.Sessions.Single(row => row.Authority.SessionId == first);
                var other = model.Sessions.Single(row => row.Authority.SessionId != first);
                original.Metadata.Should().Be(other.Metadata, "identical unnamed titles are not reader identity");
                await model.SelectAsync(original);
                await model.ReadConversationAsync();
                ReadingLayout(window);
                var scroll = interaction.FindControl<ScrollViewer>("ConversationScroll")!;
                scroll.Offset = new(0, 650);
                ReadingLayout(window);
                var anchor = ReadingAnchor(interaction);
                model.ComposerBody = "first session unsent";
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var retention = await f.Store.ReadRetentionAsync(first, f.Token);
                await model.SelectAsync(other);
                model.ConversationMessages.Should().BeEmpty();
                await model.ReadConversationAsync();
                model.ComposerBody = "other session unsent";
                ReadingLayout(window);
                scroll.Offset = new(0, 350);
                ReadingLayout(window);
                var otherAnchor = ReadingAnchor(interaction);
                var reads = reader.Reads;
                await model.SelectAsync(original);
                ReadingLayout(window);
                reader.Reads.Should().Be(reads, "switching never fetches a hidden conversation");
                model.ComposerBody.Should().Be("first session unsent");
                model.ConversationMessages.Should().BeEmpty();
                await model.ReadConversationAsync();
                ReadingLayout(window);
                var restored = ReadingAnchor(interaction);
                restored.Message.Should().Be(anchor.Message);
                restored.Offset.Should().BeApproximately(anchor.Offset, 0.1);
                interaction.IsVisible = false;
                window.Width = 1080;
                window.Height = 1150;
                ReadingLayout(window);
                interaction.IsVisible = true;
                ReadingLayout(window);
                restored = ReadingAnchor(interaction);
                restored.Message.Should().Be(anchor.Message);
                restored.Offset.Should().BeApproximately(anchor.Offset, 0.1);
                await model.SelectAsync(other);
                await model.ReadConversationAsync();
                ReadingLayout(window);
                restored = ReadingAnchor(interaction);
                restored.Message.Should().Be(otherAnchor.Message);
                restored.Offset.Should().BeApproximately(otherAnchor.Offset, 0.1);
                model.ComposerBody.Should().Be("other session unsent");
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadRetentionAsync(first, f.Token)).Should().Be(retention);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally { model.Close(); window.Close(); }
        });
    }

    [Fact]
    public async Task ConversationReaderBackgroundCompletionPreservesFocusedComposerAndOldPageWithoutReadingOrSaving()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await SeedReadingMessages(f, f.Request.SessionId, 8);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync);
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter, conversationReader: reader);
            var model = composition.Model;
            var interaction = new SessionInteractionView { DataContext = model };
            var window = ReadingWindow(interaction);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                ReadingLayout(window);
                var original = model.ConversationMessages.ToArray();
                var composer = interaction.FindControl<TextBox>("SessionComposer")!;
                model.ComposerBody = "  independent focused composer e\u0301\r\n untouched  ";
                var unsent = model.ComposerBody;
                composer.Focus().Should().BeTrue();
                var result = await composition.Conversation.SubmitAsync(model.SelectedSessionRecord!,
                    "Independently admitted request completes while composing.", static () => true, f.Token);
                ReadingLayout(window);
                model.ConversationMessages.Should().Equal(original);
                model.ConversationUpdates.Should().ContainSingle().Which.Should().Be(result.Message);
                model.ComposerBody.Should().Be(unsent);
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(composer);
                var updates = model.ConversationUpdates;
                var action = interaction.FindControl<ItemsControl>("ConversationUpdateItems")!
                    .GetVisualDescendants().OfType<Button>().Single(button =>
                        string.Equals(button.Name, "ReplyToAssistantAnswer", StringComparison.Ordinal));
                action.Focus().Should().BeTrue();
                model.ComposerBody = unsent + "still independent";
                await model.RefreshWorkAsync();
                ReadingLayout(window);
                model.ConversationUpdates.Should().BeSameAs(updates, "passive notifications cannot recreate a focused receipt");
                window.FocusManager.GetFocusedElement().Should().BeSameAs(action);
                reader.Reads.Should().Be(1);
                adapter.Calls.Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
            }
            finally { model.Close(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("sessions")]
    [InlineData("compact")]
    public async Task ConversationReaderCompletionKeepsExactClarificationAndContextInspectionWithoutReplacingOldPage(string surface)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await SeedReadingMessages(f, f.Request.SessionId, 8);
            var reader = new SearchReader(f.Store.ReadConversationPageAsync,
                readMetadata: f.Store.ReadMetadataAsync, readMessage: f.Store.ReadConversationMessageAsync);
            var adapter = new MemoryAnswerAdapter();
            var opened = new List<AdmittedDetailContent>();
            await using var composition = new Composition(f, (content, _, _, _) =>
            {
                opened.Add(content);
                return Task.FromResult("Exact context metadata opened");
            }, adapter: adapter, conversationReader: reader);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            var interaction = new SessionInteractionView { DataContext = model };
            Window window = surface switch
            {
                "sessions" => new SessionsWindow(model, owner),
                "compact" => new CompactSessionWindow(model, owner),
                _ => ReadingWindow(interaction),
            };
            window.Show();
            var published = new TaskCompletionSource<ConversationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Published(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (string.Equals(args.PropertyName, nameof(SessionsViewModel.ConversationUpdates), StringComparison.Ordinal)
                    && model.ConversationUpdates.Count == 1)
                { published.TrySetResult(model.ConversationUpdates[0]); }
            }
            model.PropertyChanged += Published;
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                var original = model.ConversationMessages.ToArray();
                var result = await composition.Conversation.SubmitAsync(model.SelectedSessionRecord!,
                    "Independent completion remains separate from the admitted page.", static () => true, f.Token);
                var completion = await published.Task.WaitAsync(f.Token);
                model.ComposerBody = "independent unsent input";
                ReadingLayout(window);
                model.ConversationUpdates.Should().ContainSingle().Which.Should().BeSameAs(completion);
                completion.Should().BeSameAs(result.Message);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var tasks = f.Count("host_task_events");
                var history = f.Count("session_history");
                var retention = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
                var exchange = window.GetVisualDescendants().OfType<ClarificationExchangeView>().Single(control =>
                    ReferenceEquals(control.DataContext, completion));
                exchange.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "ShowClarificationExchange", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await exchange.Operation;
                model.CurrentClarificationExchange.Should().NotBeNull(model.ConversationStatus);
                model.CurrentClarificationExchange!.Message.Should().BeSameAs(completion);
                model.CurrentClarificationExchange.Page.Should().NotBeNull(model.ConversationStatus);
                var context = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    string.Equals(button.Name, "OpenSubmittedAnswerContext", StringComparison.Ordinal)
                    && ReferenceEquals(button.DataContext, completion));
                context.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await (surface is "primary" ? interaction.Operation : owner.DetailOperation);
                opened.Should().ContainSingle().Which.Origin.Should().Be(DetailContentOrigin.RetainedAnswerContext);
                opened[0].ConversationMessage.Should().Be(completion);
                opened[0].Source.Should().Contain("Unknown / not recorded").And.NotContain(completion.AssistantResponse!.Body);
                model.LastDetailMessage.Should().BeSameAs(completion);
                model.LastDetailContext.Should().BeTrue();
                if (surface is "primary")
                {
                    await interaction.ShowAvailableDetailsAsync(model);
                    opened.Should().HaveCount(2);
                    opened[1].IsSameSnapshot(opened[0]).Should().BeTrue("reopening must not substitute answer prose");
                }
                model.ConversationMessages.Should().Equal(original);
                model.ConversationUpdates.Should().ContainSingle().Which.Should().BeSameAs(completion);
                model.ComposerBody.Should().Be("independent unsent input");
                reader.Reads.Should().Be(1);
                adapter.Calls.Should().Be(1);
                f.Count("host_task_events").Should().Be(tasks);
                f.Count("session_history").Should().Be(history);
                f.Count("conversation_drafts").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(retention);
                composition.Access.ControlRevision++;
                model.CurrentClarificationExchange.Should().BeNull();
                model.LastDetailMessage.Should().BeNull();
            }
            finally { model.PropertyChanged -= Published; model.Close(); window.Close(); }
        });
    }

    [Fact]
    public async Task ConversationReaderChangedTerminalRevisionRequiresExplicitAnchorReopenRatherThanSubstitution()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await SeedReadingMessages(f, f.Request.SessionId, 3);
            var pendingRequest = WindowsSqliteConversationTests.Fresh(f.Request.SessionId);
            using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(pendingRequest,
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
            await f.Store.RetainConversationMessageAsync(pendingRequest, new(1), 0,
                "Pending original with an immutable request identity.", static () => true, f.Token);
            await SeedReadingMessages(f, f.Request.SessionId, 5, pendingRequest);
            await using var composition = new Composition(f);
            var model = composition.Model;
            var interaction = new SessionInteractionView { DataContext = model };
            var window = ReadingWindow(interaction);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                await model.ReadConversationAsync();
                ReadingLayout(window);
                var scroll = interaction.FindControl<ScrollViewer>("ConversationScroll")!;
                var items = interaction.FindControl<ItemsControl>("ConversationMessageItems")!;
                var pending = items.ContainerFromIndex(3)!;
                var top = pending.TranslatePoint(default, scroll)!.Value.Y;
                scroll.Offset = new(0, scroll.Offset.Y + top + 5);
                ReadingLayout(window);
                ReadingAnchor(interaction).Message.Request.Should().Be(pendingRequest);
                await f.Store.CompleteConversationSubmissionAsync(pendingRequest, ModelTurnOutcome.Unavailable,
                    ModelTurnReason.UnregisteredModel, static () => true, f.Token);
                await model.ReadConversationAsync();
                ReadingLayout(window);
                interaction.FindControl<TextBlock>("ConversationReaderStatus")!.Text.Should()
                    .Contain("exact earlier message/revision").And.Contain("no replacement anchor");
                model.ConversationMessages[3].Request.Should().Be(pendingRequest);
                model.ConversationMessages[3].Revision.Should().Be(2);
                await model.ReadLatestConversationAsync();
                ReadingLayout(window);
                interaction.FindControl<TextBlock>("ConversationReaderStatus")!.Text.Should().Contain("Following");
                (scroll.Extent.Height - scroll.Viewport.Height - scroll.Offset.Y).Should().BeLessThan(1);
            }
            finally { model.Close(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("hide-return")]
    [InlineData("tab-return")]
    [InlineData("detach-return")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("close")]
    public async Task ConversationReaderLateLatestReadCannotPublishAfterOriginalPresentationRetirement(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            await SeedReadingMessages(f, f.Request.SessionId, 1);
            var page = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token);
            f.Request = InteractionStorageFixture.NewRequest();
            await f.AdmitAsync(newSession: true);
            await WindowsSqliteConversationTests.FinishInitial(f);
            var released = new TaskCompletionSource<ConversationPage>();
            var reader = new SearchReader((_, _, _, _) => new(released.Task));
            await using var composition = new Composition(f, conversationReader: reader);
            var model = composition.Model;
            var interaction = new SessionInteractionView { DataContext = model };
            var window = ReadingWindow(interaction);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == page.Session.SessionId));
                model.ComposerBody = "unsent must not follow the late page";
                ReadingLayout(window);
                interaction.FindControl<Button>("NewConversationUpdates")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var pending = interaction.Operation;
                pending.IsCompleted.Should().BeFalse();
                reader.Reads.Should().Be(1);
                switch (boundary)
                {
                    case "selection":
                        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != page.Session.SessionId));
                        break;
                    case "hide-return": window.Hide(); window.Show(); break;
                    case "tab-return":
                        var tabs = interaction.FindControl<TabControl>("ConversationTabs")!;
                        tabs.SelectedIndex = 1;
                        tabs.SelectedIndex = 0;
                        break;
                    case "detach-return": window.Content = null; window.Content = interaction; break;
                    case "privacy": composition.Access.CanInspect = false; break;
                    case "owner": composition.Access.ControlRevision++; break;
                    case "close": model.Close(); break;
                }
                released.SetResult(page);
                await pending;
                ReadingLayout(window);
                model.ConversationMessages.Should().BeEmpty();
                model.ConversationUpdates.Should().BeEmpty();
                interaction.GetVisualDescendants().OfType<TextBlock>().Should().NotContain(block => block.Text == page.Messages[0].Body);
                if (boundary is not ("privacy" or "owner" or "close"))
                { model.ComposerBody.Should().Be("unsent must not follow the late page"); }
                reader.Reads.Should().Be(1);
                reader.DraftReads.Should().Be(0);
                f.Count("conversation_messages").Should().Be(1);
                f.Count("conversation_drafts").Should().Be(0);
            }
            finally { released.TrySetResult(page); model.Close(); window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationReaderLatestRefusesChangedSnapshotOrDeniedAdmissionWithoutPartialPublication(bool deny)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await SeedReadingMessages(f, f.Request.SessionId, 2);
        var page = await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token);
        var reader = new SearchReader((_, cursor, _, _) => cursor is null
            ? ValueTask.FromResult(page with
            {
                Messages = [page.Messages[0]],
                Next = new(page.Session.SessionId, page.Session.Generation, page.Ceiling, 1, page.HeadRevision)
            })
            : ValueTask.FromException<ConversationPage>(new ConversationSnapshotChangedException()));
        await using var composition = new Composition(f, conversationReader: reader);
        var model = composition.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "held unchanged";
        var audit = WindowsSqliteConversationTests.AuditText(f);
        await model.ReadLatestConversationAsync(() => !deny);
        reader.Reads.Should().Be(deny ? 0 : 2);
        model.ConversationMessages.Should().BeEmpty();
        model.ConversationUpdates.Should().BeEmpty();
        model.ComposerBody.Should().Be("held unchanged");
        model.CanCompose.Should().BeFalse("a failed latest-source admission cannot renew composer authority");
        model.ConversationStatus.Should().Contain(deny ? "unavailable or changed" : "Terminal snapshot changed");
        WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
        f.Count("conversation_drafts").Should().Be(0);
        f.Count("host_questions").Should().Be(0);
        f.Count("scoped_grants").Should().Be(0);
    }

    private static Window ReadingWindow(SessionInteractionView view) =>
        new() { Content = view, DataContext = view.DataContext, Width = 1400, Height = 1400 };

    private static void ReadingLayout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static (ConversationMessage Message, double Offset) ReadingAnchor(SessionInteractionView view)
    {
        var scroll = view.FindControl<ScrollViewer>("ConversationScroll")!;
        var items = view.FindControl<ItemsControl>("ConversationMessageItems")!;
        for (var index = 0; index < items.ItemCount; index++)
        {
            var container = items.ContainerFromIndex(index)!;
            var point = container.TranslatePoint(default, scroll)!.Value;
            if (point.Y + container.Bounds.Height > 0 && point.Y < scroll.Viewport.Height)
            { return ((ConversationMessage)container.DataContext!, point.Y); }
        }
        throw new InvalidOperationException("No admitted conversation message is visible in the measured viewport.");
    }

    private static async Task SeedReadingMessages(InteractionStorageFixture fixture, HostId<SessionIdentity> session, int count,
        HostRequest? predecessor = null)
    {
        for (var index = 0; index < count; index++)
        {
            var body = "Earlier message " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ": " + string.Join(' ', Enumerable.Repeat("readable original context", 10));
            if (predecessor is null) { await WindowsSqliteConversationTests.Submit(fixture, session, body); }
            else
            {
                var request = WindowsSqliteConversationTests.Fresh(session);
                using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(request,
                    Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
                await fixture.Store.RetainPendingConversationMessageAsync(request, new(1), 0, body, [predecessor],
                    static () => true, fixture.Token);
                await fixture.Store.CompleteConversationSubmissionAsync(request, ModelTurnOutcome.Unavailable,
                    ModelTurnReason.UnregisteredModel, static () => true, fixture.Token);
            }
        }
    }
}
