using System.Text;
using System.Text.Json;

using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Dependencies;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task ForegroundOriginalRequestTraversesActualParserNativeReviewHostAndPrivateAnswerRetention(bool voice, bool keyboard, bool compact)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new OriginalRequestAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            composed.Audit.ExpectedOrigin = voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), syntheticVoice: true);
            await native.InitializeAsync();
            native.Main.BindSessionCommands(composed.Workspace,
                new(composed.Workspace, composed.Conversation, composed.Access, f.Time));
            native.Main.CommandText = "set assistant name to Nova";
            await native.Main.RunTypedCommand.ExecuteAsync();
            var owner = new SessionsPresentationOwner(composed.Model, native.Main);
            Window window = compact ? new CompactSessionWindow(composed.Model, owner) : new SessionsWindow(composed.Model, owner);
            window.Show();
            await ActivateOriginalRequestWindow(window);
            try
            {
                await composed.Model.RefreshAsync();
                await composed.Model.SelectAsync(composed.Model.Sessions.Single());
                composed.Model.ComposerBody = "independent unsent input";
                owner.CaptureForegroundConversationTarget().Should().NotBeNull();
                const string body = "  shutdown e\u0301\U0001f642\r\n\tDo not rewrite -- original  ";
                if (voice)
                {
                    native.Main.BindVoiceOwnershipGate(() => true);
                    await native.Main.SetVoiceConsentAsync(true);
                    var reviewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    native.Main.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName is nameof(native.Main.HasForegroundRequestReview) && native.Main.HasForegroundRequestReview)
                        { reviewed.TrySetResult(); }
                    };
                    await native.Main.BeginPushToTalkCommand.ExecuteAsync();
                    await native.Main.EndPushToTalkCommand.ExecuteAsync();
                    native.Voice.PublishTranscript("Nova, ask this conversation " + body);
                    await reviewed.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                }
                else
                {
                    native.Main.CommandText = "ask this conversation " + body;
                    await native.Main.RunTypedCommand.ExecuteAsync();
                }
                native.Main.HasForegroundRequestReview.Should().BeTrue(native.Main.ResponseBody);
                var panel = window.FindControl<SessionRequestReviewPanel>("OriginalRequestReview")!;
                panel.IsVisible.Should().BeTrue();
                var text = panel.Children.OfType<TextBox>().Single();
                text.Text.Should().EndWith(body);
                ControlAutomationPeer.CreatePeerForElement(text)!.GetProvider<IValueProvider>()!
                    .Value.Should().EndWith(body);
                f.Count("conversation_messages").Should().Be(0);
                adapter.Calls.Should().Be(0);
                var confirm = panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>()
                    .Single(button => button.Name is "ConfirmOriginalRequest");
                if (keyboard)
                {
                    window.UpdateLayout();
                    confirm.Focus().Should().BeTrue();
                    window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                    window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                }
                else { confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                await panel.Operation;
                adapter.Calls.Should().Be(1, native.Main.ResponseBody);
                var message = composed.Model.ConversationMessages.Single();
                message.Body.Should().Be(body);
                message.Request.SessionId.Should().Be(f.Request.SessionId);
                message.Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
                message.AssistantResponse!.Body.Should().Be("Genuine local original-request answer");
                adapter.Context.Should().Contain("shutdown").And.NotContain("independent unsent input");
                using var context = JsonDocument.Parse(adapter.Context!);
                context.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
                context.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(0);
                composed.Model.ComposerBody.Should().Be("independent unsent input");
                (await f.Store.ReadConversationMessageAsync(f.Request.SessionId, message.Request.RequestId, f.Token))
                    .Should().Be(message);
                f.Count("host_questions").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                composed.Turns.IsQuiescent.Should().BeTrue();
                await native.Main.ConfirmForegroundRequestAsync();
                adapter.Calls.Should().Be(1);
                f.Count("conversation_messages").Should().Be(1);
                WindowsSqliteConversationTests.AuditText(f).Should().NotContain(body)
                    .And.NotContain("Genuine local original-request answer");

                window.FindControl<ListBox>("AnswerSourceRecords")!.ScrollIntoView(message);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var reply = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Name is "ReplyToAssistantAnswer" && ReferenceEquals(button.DataContext, message));
                reply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.ReplyOperation;
                composed.Model.HasAssistantReply.Should().BeTrue(composed.Model.ConversationStatus);
                composed.Model.ReplySourceText.Should().Contain(message.AssistantResponse.Body);
                composed.Model.ComposerBody.Should().Be("independent unsent input");
                await composed.Model.ReviewAssistantReplyAsync();
                composed.Model.CanConfirmAssistantReply.Should().BeTrue(composed.Model.ConversationStatus);
                var originalTarget = owner.CaptureForegroundConversationTarget()!;
                window.Hide();
                window.Show();
                await ActivateOriginalRequestWindow(window);
                originalTarget.IsCurrent.Should().BeFalse();
                await composed.Model.ConfirmAssistantReplyAsync();
                composed.Model.CanConfirmAssistantReply.Should().BeFalse();
                composed.Model.CancelAssistantReply();
                composed.Model.ComposerBody.Should().Be("independent unsent input");
                adapter.Calls.Should().Be(1, "a retired reply presenter cannot submit or fall back to plain input");
                f.Count("conversation_messages").Should().Be(1);
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { owner.Revoke(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForegroundOriginalLocalRequestNeverFallsBackToConfiguredHostedProvider(bool hostedPreferred)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        var runtime = new CopilotConversationRuntime();
        await using var composed = new Composition(f, copilot: hostedPreferred ? runtime : null);
        var service = new SessionRequestCommandService(composed.Workspace, composed.Conversation, composed.Access, f.Time);
        var entry = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
        await service.ExecuteAsync(SessionCommand.Parse("ask this conversation original local request", "Nova")!,
            RequestOrigin.LocalUi, () => true, f.Token, new(entry, () => true));
        var result = await service.ExecuteAsync(new(SessionCommandOperation.RequestConfirm) { RequestUsesPendingReview = true },
            RequestOrigin.LocalUi, () => true, f.Token);
        result.RequestSubmission!.Message.Outcome.Should().Be(hostedPreferred ? ModelTurnOutcome.Denied : ModelTurnOutcome.Unavailable);
        result.RequestSubmission.Message.Reason.Should().Be(hostedPreferred ? ModelTurnReason.InvalidPolicy : ModelTurnReason.UnregisteredModel);
        result.RequestSubmission.Message.AssistantResponse.Should().BeNull();
        result.RequestSubmission.Message.Body.Should().Be("original local request");
        runtime.Sends.Should().Be(0);
        composed.Turns.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForegroundOriginalRequestStopJoinsActualHostBeforeTerminalAndSuppressesLateAnswer(bool retireSource)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new GateableConversationAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            native.Main.BindSessionCommands(composed.Workspace,
                new(composed.Workspace, composed.Conversation, composed.Access, f.Time));
            var owner = new SessionsPresentationOwner(composed.Model, native.Main);
            var window = new SessionsWindow(composed.Model, owner);
            window.Show();
            await ActivateOriginalRequestWindow(window);
            try
            {
                await composed.Model.RefreshAsync();
                await composed.Model.SelectAsync(composed.Model.Sessions.Single());
                composed.Model.ComposerBody = "independent unsent input";
                var target = composed.Model.SelectedSessionRecord!;
                native.Main.CommandText = "ask this conversation original stop request";
                await native.Main.RunTypedCommand.ExecuteAsync();
                var panel = window.FindControl<SessionRequestReviewPanel>("OriginalRequestReview")!;
                panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>()
                    .Single(button => button.Name is "ConfirmOriginalRequest").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var submission = panel.Operation;
                var provenance = await adapter.Started.Task.WaitAsync(f.Token);
                var status = await composed.Conversation.ReadRunningConversationAsync(target, () => true, f.Token);
                await composed.Conversation.CancelRunningConversationAsync(status!.Target, () => true, f.Token);
                if (retireSource) { window.Hide(); }
                submission.IsCompleted.Should().BeFalse();
                composed.Turns.IsQuiescent.Should().BeFalse();
                composed.Model.DecideComposerDraft = () => Task.FromResult(ComposerDraftDecision.Cancel);
                var shutdown = owner.PrepareShutdown();
                shutdown.IsCompleted.Should().BeFalse("voluntary shutdown joins the actual owned native operation");
                adapter.Release.SetResult(new(ModelTurnOutcome.Succeeded, provenance.Selection, new("forbidden late answer", null)));
                await submission;
                (await shutdown).Should().BeFalse("independent unsent input remains protected after the join");
                var retained = await f.Store.ReadConversationMessageAsync(provenance.Request.SessionId, provenance.Request.RequestId, f.Token);
                retained.AssistantResponse.Should().BeNull();
                if (!retireSource) { retained.Outcome.Should().Be(ModelTurnOutcome.Cancelled); }
                composed.Turns.IsQuiescent.Should().BeTrue();
                composed.Model.ComposerBody.Should().Be("independent unsent input");
            }
            finally
            {
                adapter.Release.TrySetResult(new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate));
                window.ClosePrivate();
            }
        });
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("focus")]
    [InlineData("selection")]
    [InlineData("control")]
    [InlineData("readonly")]
    [InlineData("done")]
    [InlineData("deleted")]
    [InlineData("rename")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task ForegroundReviewRetiresRatherThanRetargetingOrConsumingUnsentInput(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new OriginalRequestAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            using var native = new NativeUxFixtureSession(Path.GetFullPath("."), silentCaption: true);
            await native.InitializeAsync();
            native.Main.BindSessionCommands(composed.Workspace,
                new(composed.Workspace, composed.Conversation, composed.Access, f.Time));
            var model = composed.Model;
            var owner = new SessionsPresentationOwner(model, native.Main);
            var window = new SessionsWindow(model, owner);
            window.Show();
            await ActivateOriginalRequestWindow(window);
            var other = new CompactSessionWindow(model, owner);
            try
            {
                await model.RefreshAsync();
                await model.SelectAsync(model.Sessions.Single());
                model.ComposerBody = "original unsent";
                var target = model.SelectedSessionRecord!;
                native.Main.CommandText = "ask this conversation original";
                await native.Main.RunTypedCommand.ExecuteAsync();
                native.Main.HasForegroundRequestReview.Should().BeTrue(native.Main.ResponseBody);
                var originalSource = owner.CaptureForegroundConversationTarget()!;
                if (change is "hide") { window.Hide(); window.Show(); await ActivateOriginalRequestWindow(window); }
                if (change is "focus")
                {
                    other.Show();
                    await ActivateOriginalRequestWindow(other);
                    await ActivateOriginalRequestWindow(window);
                }
                if (change is "selection")
                {
                    var second = await composed.Workspace.CreateAsync(new("second topic"), RequestOrigin.LocalUi, f.Token);
                    await model.RefreshAsync();
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority == second.Authority));
                    model.ComposerBody = "second unsent";
                }
                if (change is "control") { composed.Access.ControlRevision++; }
                if (change is "readonly") { composed.Access.CanControl = false; }
                if (change is "done") { await composed.Workspace.ChangeLifecycleAsync(target.Authority.SessionId, target.Authority.Generation, false, RequestOrigin.LocalUi, f.Token); }
                if (change is "deleted")
                {
                    using var deletion = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                        HostActivityLayer.Application, HostOperation.Request);
                    var preview = await composed.Workspace.PreviewDispositionAsync(target.Authority.SessionId,
                        target.Authority.Generation, target.Metadata?.Revision.Value ?? 0, f.Token);
                    await composed.Workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, f.Token);
                }
                if (change is "rename") { await composed.Workspace.RenameAsync(target.Authority.SessionId, target.Authority.Generation, target.Metadata?.Revision.Value ?? 0, new("renamed topic"), RequestOrigin.LocalUi, f.Token); }
                if (change is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
                if (change is "cancel") { await native.Main.CancelForegroundRequestAsync(); }
                await native.Main.ConfirmForegroundRequestAsync();
                f.Count("conversation_messages").Should().Be(0);
                adapter.Calls.Should().Be(0);
                if (change is "hide" or "focus") { originalSource.IsCurrent.Should().BeFalse("focus/visibility ABA cannot renew original authority"); }
                if (change is "selection")
                {
                    model.ComposerBody.Should().Be("second unsent");
                    await model.SelectAsync(model.Sessions.Single(row => row.Authority.SessionId == target.Authority.SessionId));
                    model.ComposerBody.Should().Be("original unsent");
                }
                composed.Turns.IsQuiescent.Should().BeTrue();
            }
            finally { other.Close(); window.ClosePrivate(); }
        });
    }

    private static async Task ActivateOriginalRequestWindow(Window window)
    {
        var active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Window.IsActiveProperty && window.IsActive) { active.TrySetResult(); }
        }
        window.PropertyChanged += Changed;
        try
        {
            window.Activate();
            if (!window.IsActive) { await active.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken); }
        }
        finally { window.PropertyChanged -= Changed; }
    }

    private sealed class OriginalRequestAdapter : IModelTurnAdapter
    {
        internal int Calls { get; private set; }
        internal string? Context { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Calls++;
            Context = Encoding.UTF8.GetString(context.Span);
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection,
                new("Genuine local original-request answer", null)));
        }

    }

}
