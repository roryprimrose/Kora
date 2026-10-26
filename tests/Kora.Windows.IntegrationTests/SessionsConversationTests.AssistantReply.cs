using System.Text.Json;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NativeExactAnswerReplyReviewsUnchangedComposerThenRetainsGenuineLocalAnswer(int surface)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new MemoryAnswerAdapter();
            await using var composition = new Composition(f, adapter: adapter);
            var model = composition.Model;
            var owner = new SessionsPresentationOwner(model);
            var primary = surface == 2;
            var interaction = new SessionInteractionView { DataContext = model };
            Window window = primary ? new Window { Content = interaction, DataContext = model, Width = 1200, Height = 1000 }
                : surface == 1 ? new CompactSessionWindow(model, owner) : new SessionsWindow(model, owner);
            window.Show();
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                var target = model.SelectedSessionRecord!;
                model.ComposerBody = "Earlier original USER must not enter the reply envelope.";
                await model.SubmitConversationAsync();
                adapter.Next = () => Task.FromResult(new Kora.Application.Dependencies.ModelProviderReply(
                    ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("A different latest answer, never substitute this.", null)));
                model.ComposerBody = "Another independent question in this conversation.";
                await model.SubmitConversationAsync();
                adapter.Next = null;
                await model.ReadConversationAsync();
                var source = model.ConversationMessages[0];
                const string followup = "  expand the second point\r\n e\u0301  ";
                var composer = primary ? interaction.FindControl<TextBox>("SessionComposer")!
                    : window.FindControl<TextBox>("ConversationComposer")!;
                composer.Text = followup;
                if (!primary) { window.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(source); }
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var reply = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "ReplyToAssistantAnswer", StringComparison.Ordinal)
                        && ReferenceEquals(button.DataContext, source));
                reply.DataContext.Should().BeSameAs(source);
                reply.IsEnabled.Should().BeTrue();
                AutomationProperties.GetName(reply).Should().Contain("exact");
                reply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await (primary ? interaction.Operation : owner.ReplyOperation);
                model.HasAssistantReply.Should().BeTrue(model.ConversationStatus);
                model.ComposerBody.Should().Be(followup);
                model.ReplySourceText.Should().Contain(source.AssistantResponse!.Body).And.NotContain(source.Body);
                adapter.Calls.Should().Be(2, "choosing Reply is not model execution");
                var panel = window.GetVisualDescendants().OfType<AssistantReplyPanel>().Single();
                await model.SubmitConversationAsync();
                model.CanConfirmAssistantReply.Should().BeTrue(model.ConversationStatus);
                model.ReplyReviewText.Should().Contain(source.AssistantResponse.Body).And.Contain(followup)
                    .And.NotContain(source.Body).And.Contain("1 / 16 evidence items");
                f.Count("conversation_messages").Should().Be(2);
                adapter.Calls.Should().Be(2);
                var confirm = panel.GetVisualDescendants().OfType<Button>()
                    .Single(button => string.Equals(button.Name, "ConfirmAssistantReply", StringComparison.Ordinal));
                confirm.IsEnabled.Should().BeTrue();
                if (primary)
                {
                    confirm.Focus().Should().BeTrue();
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                }
                else { confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                await panel.Operation;
                adapter.Calls.Should().Be(3);
                var retained = (await f.Store.ReadConversationPageAsync(target.Authority.SessionId, null, 25, f.Token)).Messages;
                retained.Should().HaveCount(3);
                retained[0].Should().Be(source);
                retained[2].Body.Should().Be(followup);
                retained[2].Request.SessionId.Should().Be(target.Authority.SessionId);
                retained[2].Generation.Should().Be(target.Authority.Generation);
                retained[2].AssistantResponse!.Body.Should().Be("Answer from reviewed session fact.");
                retained[2].Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                using var wire = JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(followup);
                wire.RootElement.GetProperty("ToolMode").GetInt32().Should().Be((int)ModelToolMode.None);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().Single();
                evidence.GetProperty("Disclosure").GetInt32().Should().Be((int)ModelEvidenceDisclosure.LocalOnly);
                using var content = JsonDocument.Parse(evidence.GetProperty("Content").GetString()!);
                content.RootElement.GetProperty("Role").GetString().Should().Be("ASSISTANT");
                content.RootElement.GetProperty("Sequence").GetInt64().Should().Be(source.Sequence);
                content.RootElement.GetProperty("Body").GetString().Should().Be(source.AssistantResponse.Body);
                model.HasAssistantReply.Should().BeFalse();
                model.ComposerBody.Should().BeEmpty();
                await model.ConfirmAssistantReplyAsync();
                adapter.Calls.Should().Be(3, "confirmation cannot be replayed");
                f.Count("scoped_grants").Should().Be(0);
                f.Count("host_questions").Should().Be(0);
                f.Count("reviewed_memory").Should().Be(0);
                (await f.Store.ReadConversationDraftAsync(target.Authority.SessionId, f.Token)).Draft.Body.Should().BeNull();
            }
            finally { model.Close(); window.Close(); }
        });
    }

    [Theory]
    [InlineData("body")]
    [InlineData("page")]
    [InlineData("cancel")]
    [InlineData("selection")]
    [InlineData("privacy")]
    [InlineData("hosted")]
    [InlineData("wrong-row")]
    [InlineData("presenter")]
    [InlineData("expiry")]
    [InlineData("discard")]
    [InlineData("origin")]
    public async Task ExactReplyChangesRefuseStaleSendWithoutPlainBodyFallbackAndPreserveUnsentPolicy(string change)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var original = f.Request.SessionId;
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new MemoryAnswerAdapter();
        await using var composition = new Composition(f, adapter: adapter);
        var model = composition.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
        model.ComposerBody = "original";
        await model.SubmitConversationAsync();
        await model.ReadConversationAsync();
        var source = model.ConversationMessages.Single();
        model.ComposerBody = "expand the second point";
        var presenter = true;
        await model.ReplyToAnswerAsync(source, () => presenter);
        await model.ReviewAssistantReplyAsync();
        model.CanConfirmAssistantReply.Should().BeTrue(model.ConversationStatus);
        if (change is "body") { model.ComposerBody += " unchanged source, edited request"; }
        if (change is "page") { await model.ReadConversationAsync(); }
        if (change is "cancel") { model.CancelAssistantReply(); }
        if (change is "presenter") { presenter = false; }
        if (change is "expiry") { f.Time.Now = f.Time.Now.AddMinutes(1); }
        if (change is "discard")
        {
            model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Discard);
            await model.DiscardComposerBufferAsync();
        }
        if (change is "privacy") { composition.Access.CanInspect = false; composition.Access.ControlRevision++; }
        if (change is "hosted") { await model.SubmitCopilotConversationAsync(); }
        if (change is "origin")
        {
            using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(
                Kora.Core.Hosting.HostRequest.Create(Kora.Core.Hosting.RequestOrigin.HostSystem),
                Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
            await model.ConfirmAssistantReplyAsync();
            model.ConversationStatus.Should().Contain("original native user authority");
        }
        if (change is "wrong-row")
        {
            await model.ReplyToAnswerAsync(source with { });
            model.ConversationStatus.Should().Contain("currently displayed");
            model.CancelAssistantReply();
        }
        if (change is "selection")
        {
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId != original));
            model.ComposerBody = "other session untouched";
            await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == original));
            model.HasAssistantReply.Should().BeTrue("returning must not silently make a stale reply ordinary input");
            await model.SubmitConversationAsync();
        }
        if (change is not ("hosted" or "origin")) { await model.ConfirmAssistantReplyAsync(); }
        adapter.Calls.Should().Be(1);
        f.Count("conversation_messages").Should().Be(1);
        model.ComposerBody.Should().Be(change is "privacy" or "discard" ? string.Empty
            : change is "body" ? "expand the second point unchanged source, edited request" : "expand the second point");
        if (change is "hosted")
        {
            model.ConversationStatus.Should().Contain("Reply mode requires");
            model.CancelAssistantReply();
        }
        if (change is "page")
        {
            model.ReplyReviewText.Should().BeEmpty();
            await model.SubmitConversationAsync();
            adapter.Calls.Should().Be(1);
            model.HasAssistantReply.Should().BeTrue();
            model.CancelAssistantReply();
            await model.SubmitConversationAsync();
            adapter.Calls.Should().Be(2, "explicit Cancel restores ordinary submission, not before");
        }
        model.Close();
    }

    [Fact]
    public async Task CancelExactReplySuppressesLateAnswerAndJoinsNonCooperativeProvider()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var adapter = new MemoryAnswerAdapter();
        await using var composition = new Composition(f, adapter: adapter);
        var model = composition.Model;
        await model.RefreshAsync();
        await model.SelectAsync(model.Sessions.Single());
        model.ComposerBody = "original";
        await model.SubmitConversationAsync();
        await model.ReadConversationAsync();
        await model.ReplyToAnswerAsync(model.ConversationMessages.Single());
        model.ComposerBody = "expand the second point";
        await model.ReviewAssistantReplyAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Kora.Application.Dependencies.ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.Next = () => { entered.SetResult(); return release.Task; };
        var sending = model.ConfirmAssistantReplyAsync();
        await entered.Task.WaitAsync(f.Token);
        model.CancelAssistantReply();
        await sending;
        composition.Turns.IsQuiescent.Should().BeFalse("cancel is not resource quiescence");
        model.ComposerBody.Should().Be("expand the second point");
        release.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late forbidden answer", null)));
        await composition.Turns.DisposeAsync();
        model.ConversationMessages.Should().NotContain(row => row.AssistantResponse != null && row.AssistantResponse.Body == "late forbidden answer");
        var retained = (await f.Store.ReadConversationPageAsync(model.SelectedSessionRecord!.Authority.SessionId, null, 25, f.Token)).Messages;
        retained.Should().NotContain(row => row.AssistantResponse != null && row.AssistantResponse.Body == "late forbidden answer");
        model.ComposerBody.Should().Be("expand the second point");
        model.Close();
    }
}
