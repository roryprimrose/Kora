using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;
using Kora.Core.Providers;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData("close")]
    [InlineData("privacy")]
    [InlineData("memory")]
    [InlineData("history")]
    [InlineData("answer")]
    public async Task MixedCanonicalAnswerReopensInExactNativeInspectorWithoutInstructionReadsOrRenewedAuthority(string retirement)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Local, [HandoffCatalogue.Local]));
            var source = new NativeInstructionSource();
            var snapshot = source.Snapshot!;
            var revisions = new InstructionRevisionCatalogue(f.Paths);
            var catalogue = new ArtifactCatalogue(revisions.Reconcile(
                [snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision }]));
            revisions.CompleteActivation(snapshot, revisions.PrepareActivation(snapshot), catalogue);
            var adapter = new MemoryAnswerAdapter();
            ConversationMessage retained;
            ConversationMessage earlier;
            Guid memory;
            await using (var first = new Composition(f, adapter: adapter, memoryContext: true,
                localRequestCatalogue: new HandoffCatalogue(f),
                instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time)))
            {
                var model = first.Model;
                await PrepareMemoryContext(model);
                memory = model.MemoryRecords.Single().Id;
                model.ComposerBody = "earlier mixed source user marker";
                await model.SubmitConversationAsync();
                await model.ReadConversationAsync();
                earlier = model.ConversationMessages.Single();
                model.OpenMixedContext();
                model.SelectMixedHistory(model.MixedHistoryChoices.Reverse().ToArray());
                model.SelectMixedMemories(model.MixedMemoryChoices);
                model.MixedInstruction = model.MixedInstructionChoices.Single();
                model.MixedBody = "exact mixed follow-up marker";
                await model.ReviewMixedContextAsync();
                model.CanConfirmMixedContext.Should().BeTrue(model.ConversationStatus);
                await model.ConfirmMixedContextAsync();
                retained = model.ConversationUpdates.Single();
                retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded, model.ConversationStatus);
                var metadata = retained.SubmittedContext!;
                metadata.Version.Should().Be(4);
                metadata.SourceCount.Should().Be(4);
                metadata.InstructionSource!.Revision.Should().Be(snapshot.Revision);
                metadata.InstructionSource.Digest.Should().Be(snapshot.Document.Definition.Digest);
                metadata.MemorySources.Single().Id.Value.Should().Be(memory);
                metadata.CanonicalUtf8Bytes.Should().Be(adapter.Wire!.Length);
                JsonSerializer.Serialize(metadata).Should().NotContain(snapshot.Name).And.NotContain(snapshot.Document.Text)
                    .And.NotContain("private reviewed session decision").And.NotContain(earlier.Body);
                using var wire = JsonDocument.Parse(adapter.Wire);
                foreach (var item in wire.RootElement.GetProperty("Evidence").EnumerateArray())
                {
                    item.TryGetProperty("InstructionSource", out _).Should().BeFalse();
                    item.TryGetProperty("MemorySource", out _).Should().BeFalse();
                }
                model.Close();
            }
            var reads = source.Reads;
            source.Snapshot = null;
            catalogue.ReportReloadFailure(new IOException("Unavailable source catalogue"));
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var reopened = await f.Store.ReadConversationMessageAsync(retained.Request.SessionId, retained.Request.RequestId, f.Token);
            reopened.Should().Be(retained);
            (await f.Store.ReadSubmittedContextAvailabilityAsync(reopened, f.Token)).Should().Equal(
                SubmittedHistoryAvailability.RetainedRevision, SubmittedHistoryAvailability.RetainedRevision,
                SubmittedHistoryAvailability.Unknown, SubmittedHistoryAvailability.RetainedRevision);
            var opened = new List<AdmittedDetailContent>();
            Func<CancellationToken, Task<AdmittedDetailContent>>? resolve = null;
            DetailViewerState? reader = null;
            DetailWindow? detailWindow = null;
            Window? detailOwner = null;
            var clipboard = new ReaderRetirementClipboard();
            using var details = new DetailWindowController(new EmbeddedUserDocumentationProvider(), () => true,
                new(NullLogger<NativeDetailRenderer>.Instance), clipboard, (observed, result, copy) =>
                {
                    reader = observed;
                    return detailWindow = new DetailWindow(observed, result, copy);
                }, NullLogger<DetailWindowController>.Instance);
            await using var composition = new Composition(f, (content, admitted, revalidate, token) =>
            {
                opened.Add(content); resolve = revalidate;
                return details.OpenConversationDetailAsync(content, detailOwner, admitted, revalidate, token);
            }, adapter: adapter, instructionFactory: access => new(catalogue, revisions, source, f.Store, access, f.Time),
                revokeDetails: details.RevokeConversationSession);
            var state = composition.Model;
            await state.RefreshAsync();
            await state.SelectAsync(state.Sessions.Single());
            await state.ReadConversationAsync();
            var owner = new SessionsPresentationOwner(state);
            var window = new SessionsWindow(state, owner);
            detailOwner = window;
            window.Show();
            try
            {
                var clock = await f.Store.ReadRetentionAsync(retained.Request.SessionId, f.Token);
                var audit = WindowsSqliteConversationTests.AuditText(f);
                var tasks = f.Count("host_tasks");
                state.ComposerBody = "independent unsent marker";
                var selected = state.ConversationMessages.Single(item => item.Request == retained.Request);
                window.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(selected);
                window.UpdateLayout();
                var button = window.GetVisualDescendants().OfType<Button>().Single(control =>
                    string.Equals(control.Name, "OpenSubmittedAnswerContext", StringComparison.Ordinal)
                    && control.DataContext is ConversationMessage message && message.Request == retained.Request);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.DetailOperation;
                var detail = opened.Should().ContainSingle().Subject;
                detail.Origin.Should().Be(DetailContentOrigin.RetainedAnswerContext);
                detail.ConversationMessage.Should().Be(retained);
                detail.Source.Should().Contain("1. Earlier USER").And.Contain("2. Earlier ASSISTANT")
                    .And.Contain("3. Selected owned INSTRUCTION").And.Contain("4. Selected SESSION memory")
                    .And.Contain("Unknown; current instruction metadata was not observed")
                    .And.Contain("exact enabled memory revision is retained")
                    .And.NotContain(snapshot.Name).And.NotContain(snapshot.Document.Text)
                    .And.NotContain(earlier.Body).And.NotContain(retained.AssistantResponse!.Body)
                    .And.NotContain("private reviewed session decision");
                (await resolve!(f.Token)).IsSameSnapshot(detail).Should().BeTrue();
                state.ComposerBody.Should().Be("independent unsent marker");
                adapter.Calls.Should().Be(2);
                source.Reads.Should().Be(reads, "inspection must not read a withdrawn instruction or refresh its catalogue");
                (await f.Store.ReadRetentionAsync(retained.Request.SessionId, f.Token)).Should().Be(clock);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("host_tasks").Should().Be(tasks);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                reader.Should().NotBeNull();
                reader!.Content.Should().NotBeNull(state.ConversationStatus);
                detailWindow!.IsVisible.Should().BeTrue(state.ConversationStatus);
                detailWindow.FindControl<TextBox>("ContentReader")!.Text.Should().Be(detail.Source);
                reader!.TryGetCopySource(true, true, out _).Should().BeFalse();
                if (retirement is "privacy")
                { composition.Access.CanControl = false; composition.Access.ControlRevision++; }
                else if (retirement is "close") { window.ClosePrivate(); }
                else
                {
                    using var connection = f.OpenRaw();
                    using var command = connection.CreateCommand();
                    command.CommandText = retirement is "memory"
                        ? "DELETE FROM reviewed_memory WHERE memory_id=$id;"
                        : "DELETE FROM conversation_messages WHERE request_id=$id;";
                    command.Parameters.AddWithValue("$id", retirement is "memory" ? memory.ToString("D")
                        : (retirement is "history" ? earlier.Request.RequestId : retained.Request.RequestId).Value.ToString("D"));
                    command.ExecuteNonQuery().Should().Be(1);
                }
                await resolve.Invoking(revalidate => revalidate!(f.Token)).Should().ThrowAsync<Exception>();
                await details.RevalidateConversationDetailsAsync();
                reader.Content.Should().BeNull();
                detailWindow.IsVisible.Should().BeFalse();
                detailWindow.FindControl<TextBox>("ContentReader")!.Text.Should().BeEmpty();
                clipboard.Writes.Should().Be(0);
                source.Reads.Should().Be(reads);
                adapter.Calls.Should().Be(2);
            }
            finally { details.ClearForPrivacy(); window.ClosePrivate(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrimaryContextInspectorBackControlFocusesExactAnswerWithoutReplacingComposerOrSubmitting(bool memory)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Local, [HandoffCatalogue.Local]));
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter, memoryContext: memory,
                localRequestCatalogue: new HandoffCatalogue(f));
            string sourceBody;
            string sourceAnswer;
            ConversationMessage retained;
            if (memory)
            {
                await PrepareMemoryContext(composed.Model);
                sourceBody = "private reviewed session decision";
                sourceAnswer = sourceBody;
                composed.Model.SelectMemoryContext([composed.Model.MemoryRecords.Single()]);
                composed.Model.MemoryContextRequest = "genuine retained follow-up request";
                await composed.Model.ReviewMemoryContextAsync();
                await composed.Model.ConfirmMemoryContextAsync();
                retained = composed.Model.ConversationMessages.Single();
            }
            else
            {
                await composed.Model.RefreshAsync();
                await composed.Model.SelectAsync(composed.Model.Sessions.Single());
                composed.Model.ComposerBody = "genuine original request";
                await composed.Model.SubmitConversationAsync();
                var source = composed.Model.ConversationMessages.Single();
                sourceBody = source.Body;
                sourceAnswer = source.AssistantResponse!.Body;
                await composed.Model.ReadConversationAsync();
                composed.Model.OpenHistoryContext();
                composed.Model.SelectHistoryContext(composed.Model.HistoryContextCandidates.ToArray());
                composed.Model.ComposerBody = "genuine retained follow-up request";
                await composed.Model.ReviewHistoryContextAsync();
                await composed.Model.ConfirmHistoryContextAsync();
                composed.Model.ConversationMessages.Should().ContainSingle().Which.Should().Be(source);
                retained = composed.Model.ConversationUpdates.Single();
            }
            retained.SubmittedContext.Should().NotBeNull();
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."));
            await native.InitializeAsync();
            using var details = new DetailWindowController(new EmbeddedUserDocumentationProvider(), native.Main,
                NullLogger<DetailWindowController>.Instance, NullLogger<NativeDetailRenderer>.Instance,
                NullLogger<StaticHtmlDetailRenderer>.Instance);
            using var responses = new ResponseWindowController(native.Main, NullLogger<ResponseWindowController>.Instance);
            using var sessions = new SessionsWindowController(native.Main, composed.Workspace, native.Evidence,
                composed.Access, NullLogger<SessionsViewModel>.Instance, details: details, conversation: composed.Conversation);
            ResponseWindow? window = null;
            sessions.CreateInteraction = (model, owner) =>
                window = responses.PrepareSessions(model, owner, sessions.Open, static () => { });
            sessions.Bind();
            await sessions.ShowInteractionAsync(true, static () => true);
            var state = (SessionsViewModel)window!.SessionView.DataContext!;
            await state.SelectAsync(state.Sessions.Single(entry => entry.Authority.SessionId == f.Request.SessionId));
            await state.ReadConversationAsync();
            var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var audit = WindowsSqliteConversationTests.AuditText(f);
            var tasks = f.Count("host_tasks");
            window.ShowSessions(true);
            window.UpdateLayout();
            var original = state.ConversationMessages.Single(message => message.Request == retained.Request);
            state.ComposerBody = "independent unsent input";
            var button = window.SessionView.GetVisualDescendants().OfType<Button>().Single(control =>
                string.Equals(control.Name, "OpenSubmittedAnswerContext", StringComparison.Ordinal)
                && control.DataContext is ConversationMessage message && message.Request == retained.Request);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await window.SessionView.Operation;
            window.UpdateLayout();
            window.SessionView.DetailsVisible.Should().BeTrue(state.ConversationStatus);
            var reader = window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single();
            reader.FindControl<TextBlock>("ProvenanceLabel")!.Text.Should().Contain("Historical").And.Contain("not current execution");
            reader.FindControl<TextBox>("ContentReader")!.Text.Should().Contain(memory
                    ? "exact enabled memory revision is retained" : "exact source-row revision is retained")
                .And.NotContain(sourceBody).And.NotContain(sourceAnswer)
                .And.NotContain(retained.Body).And.NotContain(retained.AssistantResponse!.Body);
            window.SessionView.FindControl<TabControl>("ConversationTabs")!.SelectedIndex = 1;
            var back = window.SessionView.FindControl<Button>("BackToConversation")!;
            back.IsEnabled.Should().BeTrue();
            back.Content.Should().Be("Back to conversation");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.IsFocused.Should().BeTrue("the return control must target this exact answer, not the latest source");
            window.SessionView.GetVisualDescendants().OfType<DetailContentView>().Single().Should().BeSameAs(reader);
            state.ComposerBody.Should().Be("independent unsent input");
            state.ConversationMessages.Single(message => message.Request == retained.Request).Should().Be(original);
            state.ConversationMessages.Should().HaveCount(memory ? 1 : 2);
            adapter.Calls.Should().Be(memory ? 1 : 2);
            (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
            WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
            f.Count("host_tasks").Should().Be(tasks);
            f.Count("host_questions").Should().Be(0);
            f.Count("scoped_grants").Should().Be(0);
            state.ComposerBody = string.Empty;
            window.Close();
        });
    }

    [Fact]
    public async Task ReviewedHistoryCanonicalDispatchFinalTransactionRestartAndNativeExactInspectionHaveNoReplayOrSourceBodyRead()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            new LocalProviderProfilePreferences(f.Paths).Save(new(ProviderService.Local, [HandoffCatalogue.Local]));
            var adapter = new HistoryAdapter();
            ConversationMessage retained;
            ConversationMessage original;
            await using (var first = new Composition(f, adapter: adapter, localRequestCatalogue: new HandoffCatalogue(f)))
            {
                var model = first.Model;
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "original source body must not enter context metadata";
                await model.SubmitConversationAsync();
                original = model.ConversationMessages.Single();
                await model.ReadConversationAsync();
                model.OpenHistoryContext();
                model.SelectHistoryContext(model.HistoryContextCandidates.ToArray());
                model.ComposerBody = "What follows from those exact earlier messages?";
                await model.ReviewHistoryContextAsync();
                await model.ConfirmHistoryContextAsync();
                model.ConversationMessages.Should().ContainSingle().Which.Should().Be(original);
                retained = model.ConversationUpdates.Single();
                retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded, model.ConversationStatus);
                retained.SubmittedContext.Should().NotBeNull();
                retained.SubmittedContext!.Profile.Should().Be(HandoffCatalogue.Local);
                retained.SubmittedContext.Sources.Should().HaveCount(2);
                retained.SubmittedContext.CanonicalUtf8Bytes.Should().Be(adapter.Wire!.Length);
                using var wire = JsonDocument.Parse(adapter.Wire);
                wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Contain(original.Body);
                wire.RootElement.GetProperty("Evidence")[0].TryGetProperty("HistorySource", out _).Should().BeFalse();
                JsonSerializer.Serialize(retained.SubmittedContext).Should().NotContain(original.Body);
                model.Close();
            }
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
            var opened = new List<AdmittedDetailContent>();
            Func<CancellationToken, Task<AdmittedDetailContent>>? revalidate = null;
            Func<bool>? live = null;
            await using var restarted = new Composition(f, (content, admitted, resolve, _) =>
            {
                opened.Add(content);
                live = admitted;
                revalidate = resolve;
                return Task.FromResult("Exact context metadata opened");
            }, adapter: adapter);
            var state = restarted.Model;
            await state.RefreshAsync();
            await state.SelectAsync(state.Sessions.Single());
            await state.ReadConversationAsync();
            var clock = await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token);
            var audit = WindowsSqliteConversationTests.AuditText(f);
            var owner = new SessionsPresentationOwner(state);
            var parent = new SessionsWindow(state, owner);
            parent.Show();
            try
            {
                var displayed = state.ConversationMessages.Single(message => message.Request == retained.Request);
                parent.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(displayed);
                parent.UpdateLayout();
                var button = parent.GetVisualDescendants().OfType<Button>().Single(control =>
                    string.Equals(control.Name, "OpenSubmittedAnswerContext", StringComparison.Ordinal)
                    && control.DataContext is ConversationMessage message && message.Request == retained.Request);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.DetailOperation;
                var context = opened.Should().ContainSingle().Subject;
                context.Origin.Should().Be(DetailContentOrigin.RetainedAnswerContext);
                context.ConversationMessage.Should().Be(retained);
                context.Source.Should().Contain("1. Earlier USER message").And.Contain("2. Earlier ASSISTANT answer")
                    .And.Contain("not transport confirmation").And.Contain("exact source-row revision is retained")
                    .And.NotContain(original.Body).And.NotContain(original.AssistantResponse!.Body)
                    .And.NotContain(retained.AssistantResponse!.Body);
                var reader = new DetailViewerState(context, live);
                reader.CompleteRender(reader.Generation, context.Source, "Native metadata", false).Should().BeTrue();
                reader.TryGetCopySource(true, true, out _).Should().BeFalse();
                reader.CanInteractWithFinalDocument.Should().BeFalse();
                (await revalidate!(f.Token)).IsSameSnapshot(context).Should().BeTrue();
                await state.OpenConversationDetailAsync(displayed, assistant: true);
                opened[^1].Source.Should().Be(retained.AssistantResponse.Body, "return targets the exact original answer, not the newest source");
                adapter.Calls.Should().Be(2);
                state.ComposerBody.Should().BeEmpty();
                (await f.Store.ReadRetentionAsync(f.Request.SessionId, f.Token)).Should().Be(clock);
                WindowsSqliteConversationTests.AuditText(f).Should().Be(audit);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                parent.ClosePrivate();
                live!().Should().BeFalse();
                reader.Content.Should().BeNull();
                await revalidate.Invoking(resolve => resolve!(f.Token)).Should().ThrowAsync<Exception>();
            }
            finally { parent.ClosePrivate(); }
        });
    }

    [Fact]
    public async Task LegacyAnswerInspectionDoesNotInventSourcesFromModelProseAndRejectsCopiedOrStaleSelections()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new HistoryAdapter();
        var opened = new List<AdmittedDetailContent>();
        await using var composed = new Composition(f, (content, _, _, _) =>
        {
            opened.Add(content);
            return Task.FromResult("Legacy metadata observed");
        }, adapter: adapter);
        var model = composed.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "ordinary request without explicit reviewed history";
        await model.SubmitConversationAsync();
        var message = model.ConversationMessages.Single();
        await model.OpenConversationDetailAsync(message, submittedContext: true);
        opened.Single().Source.Should().Contain("Unknown / not recorded").And.Contain("no host-recorded");
        await model.OpenConversationDetailAsync(message with { }, submittedContext: true);
        opened.Should().ContainSingle("equal copied data is not a native selection");
        await model.SelectAsync(null);
        await model.OpenConversationDetailAsync(message, submittedContext: true);
        opened.Should().ContainSingle("stale selection cannot publish");
        adapter.Calls.Should().Be(1);
        model.Close();
    }
}
