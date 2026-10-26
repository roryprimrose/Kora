using System.Text;
using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task NamedAgentFullCommandReviewSeparateConfirmationAndExactRetainedAnswer(bool voice, bool nativeControls, bool qualified)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: qualified ? adapter : null);
            composed.Audit.ExpectedOrigin = voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
            var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
            await composed.Workspace.RenameAsync(target.Authority.SessionId, target.Authority.Generation,
                target.Metadata?.Revision.Value ?? 0, new("Planning"), RequestOrigin.LocalUi, f.Token);
            var other = await composed.Workspace.CreateAsync(new("Elsewhere"), RequestOrigin.LocalUi, f.Token);
            using var native = new NativeUxFixtureSession(NamedAgentScratch(), syntheticVoice: voice);
            await native.InitializeAsync();
            if (voice)
            {
                native.Main.BindVoiceOwnershipGate(static () => true);
                await native.Main.SetVoiceConsentAsync(true);
                await native.Main.BeginPushToTalkAsync();
                await native.Main.EndPushToTalkAsync();
            }
            var reader = new NamedSourceReader();
            var logger = new NamedAgentLogger();
            using var catalogue = NamedCatalogue(reader);
            await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time);
            var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                () => true, () => 1, logger,
                new(catalogue, (_, _) => Task.FromResult(reader.Metadata.CanonicalPath)));
            controller.Bind();
            AgentRunWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
            const string input = "  exact original e\u0301 \r\n task  ";
            try
            {
                await NamedAgentInput(native, $"agent invoke \"existing-profile\" {input}", voice);
                var window = opened ?? throw new InvalidOperationException("No named agent review opened.");
                window.Input.Text.Should().Be(input);
                window.Source.IsReadOnly.Should().BeTrue();
                window.Input.IsReadOnly.Should().BeTrue();
                window.Confirm.IsEnabled.Should().BeFalse();
                reader.Reads.Should().Be(0, "name resolution must not read the profile body or invoke a model");
                adapter.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
                if (nativeControls)
                {
                    window.Targets.SelectedItem = window.Targets.Items.Cast<object>().Single(row =>
                        string.Equals(row.ToString(), "Planning", StringComparison.Ordinal));
                    NamedAgentButton(window, "Read source and review exact invocation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await controller.ActiveOperation;
                }
                else
                {
                    await NamedAgentInput(native, "agent conversation Planning", false);
                    await NamedAgentInput(native, "agent review invocation", false);
                }
                window.Confirm.IsEnabled.Should().BeTrue($"{window.Status.Text}; source reads {reader.Reads}; {string.Join("; ", logger.Messages)}");
                window.Review.Text.Should().Contain("CONVERSATION Planning").And.Contain(input)
                    .And.Contain(reader.Inner.Text)
                    .And.Contain(voice ? "ORIGINAL CHANNEL ActivatedVoice" : "ORIGINAL CHANNEL LocalUi")
                    .And.NotContain(f.Request.SessionId.Value.ToString("D"));
                adapter.Calls.Should().Be(0, "review is not invocation consent");
                if (nativeControls) { window.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                else { await NamedAgentInput(native, "agent confirm invocation", false); }
                await controller.ActiveOperation;
                adapter.Calls.Should().Be(qualified ? 1 : 0, $"{window.Status.Text}; {string.Join("; ", logger.Messages)}");
                window.Result.Text.Should().Be(qualified ? "earlier ASSISTANT" : "No assistant answer was produced.");
                window.Status.Text.Should().Contain(qualified ? "Retained terminal Succeeded" : "Retained terminal Unavailable");
                var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                message.Body.Should().Be(input);
                message.Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
                if (qualified) { message.AssistantResponse!.Body.Should().Be("earlier ASSISTANT"); }
                else { message.AssistantResponse.Should().BeNull(); message.Reason.Should().Be(ModelTurnReason.UnregisteredModel); }
                (await f.Store.ReadConversationPageAsync(other.Authority.SessionId, null, 25, f.Token)).Messages.Should().BeEmpty();
                if (qualified)
                {
                    using var wire = JsonDocument.Parse(adapter.Wire!);
                    wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
                }
                f.Count("scoped_grants").Should().Be(0);
                await NamedAgentInput(native, "agent confirm invocation", false);
                await controller.ActiveOperation;
                adapter.Calls.Should().Be(qualified ? 1 : 0, "a consumed confirmation cannot replay the task");
                await NamedAgentInput(native, "agent stop", false);
                window.IsVisible.Should().BeFalse();
                window.Input.Text.Should().BeEmpty();
                window.Result.Text.Should().BeEmpty();
                reader.Inner.Released.Should().BeTrue();
            }
            finally { await native.Main.ExitAsync(); }
        });
    }

    [Theory]
    [InlineData("no-confirmation")]
    [InlineData("missing-name")]
    [InlineData("duplicate-name")]
    [InlineData("disabled")]
    [InlineData("no-inspect")]
    [InlineData("source-before-review")]
    [InlineData("source-after-review")]
    [InlineData("deleted-source")]
    [InlineData("catalogue")]
    [InlineData("permission")]
    [InlineData("session-done")]
    [InlineData("session-deleted")]
    [InlineData("input")]
    [InlineData("expired")]
    public async Task NamedAgentCommandDenialsNeverInvokeFallbackOrRetainSuccess(string scenario)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            using var native = new NativeUxFixtureSession(NamedAgentScratch());
            await native.InitializeAsync();
            var reader = new NamedSourceReader();
            using var catalogue = NamedCatalogue(reader, scenario);
            await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time);
            var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                () => true, () => 1, NullLogger<AgentRunController>.Instance,
                new(catalogue, (_, _) => Task.FromResult(reader.Metadata.CanonicalPath)));
            controller.Bind();
            AgentRunWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
            try
            {
                if (scenario is "no-inspect") { composed.Access.CanInspect = false; }
                await NamedAgentInput(native, "agent invoke \"existing-profile\" original request", false);
                var window = opened ?? throw new InvalidOperationException("No named agent window.");
                if (scenario is "missing-name" or "duplicate-name" or "disabled" or "no-inspect")
                {
                    window.Confirm.IsEnabled.Should().BeFalse();
                    reader.Reads.Should().Be(0);
                    window.Status.Text.Should().Contain("Named agent unavailable");
                }
                else
                {
                    window.Targets.SelectedIndex = 0;
                    if (scenario is "source-before-review") { reader.Inner.Text += "\nChanged before exact-source review."; }
                    await NamedAgentInput(native, "agent review invocation", false);
                    if (scenario is "source-before-review") { window.Confirm.IsEnabled.Should().BeFalse(); }
                    else { window.Confirm.IsEnabled.Should().BeTrue(window.Status.Text); }
                    if (scenario is "source-after-review") { reader.Inner.Text += "\nChanged after review."; }
                    if (scenario is "deleted-source") { reader.Missing = true; }
                    if (scenario is "catalogue") { catalogue.ReportReloadFailure(new IOException("Synthetic unavailable inventory.")); }
                    if (scenario is "permission") { composed.Access.CanControl = false; }
                    if (scenario is "input") { window.Input.Text = "Different task."; }
                    if (scenario is "expired") { f.Time.Now = f.Time.Now.AddMinutes(3); }
                    if (scenario is "session-done" or "session-deleted")
                    {
                        var target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
                        await composed.Workspace.ChangeLifecycleAsync(target.Authority.SessionId, target.Authority.Generation,
                            false, RequestOrigin.LocalUi, f.Token);
                        if (scenario is "session-deleted")
                        {
                            using var deletion = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                                HostActivityLayer.Application, HostOperation.Request);
                            target = await f.Store.ReadMetadataAsync(f.Request.SessionId, f.Token);
                            var preview = await composed.Workspace.PreviewDispositionAsync(target.Authority.SessionId,
                                target.Authority.Generation, target.Metadata?.Revision.Value ?? 0, f.Token);
                            await composed.Workspace.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, f.Token);
                        }
                    }
                    if (scenario is not "no-confirmation") { await NamedAgentInput(native, "agent confirm invocation", false); }
                    await controller.ActiveOperation;
                }
                adapter.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
                f.Count("scoped_grants").Should().Be(0);
                composed.Audit.Events.Should().NotContain(item => item.ActionId == "agents.revision.invoke"
                    && item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Succeeded);
                await NamedAgentInput(native, "agent stop", false);
                window.IsVisible.Should().BeFalse();
            }
            finally { await native.Main.ExitAsync(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedAgentStopAndShutdownJoinIgnoringProviderAndSuppressItsLateAnswer(bool shutdown)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new IgnoringAgentAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            using var native = new NativeUxFixtureSession(NamedAgentScratch());
            await native.InitializeAsync();
            var reader = new NamedSourceReader();
            using var catalogue = NamedCatalogue(reader);
            await using var workflow = new BoundedAgentWorkflow(reader, f.Store, composed.Access,
                composed.Conversation, composed.Turns, composed.Audit, f.Time);
            var controller = new AgentRunController(native.Main, workflow, composed.Workspace,
                () => true, () => 1, NullLogger<AgentRunController>.Instance,
                new(catalogue, (_, _) => Task.FromResult(reader.Metadata.CanonicalPath)));
            controller.Bind();
            AgentRunWindow? opened = null;
            using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) => opened = window);
            try
            {
                await NamedAgentInput(native, "agent invoke \"existing-profile\" original retained task", false);
                var window = opened ?? throw new InvalidOperationException("No named agent window.");
                window.Targets.SelectedIndex = 0;
                await NamedAgentInput(native, "agent review invocation", false);
                window.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                var running = controller.ActiveOperation;
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                var stopping = shutdown ? native.Main.ExitAsync() : NamedAgentInput(native, "agent stop", false);
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                stopping.IsCompleted.Should().BeFalse();
                running.IsCompleted.Should().BeFalse();
                reader.Inner.Released.Should().BeFalse();
                window.IsVisible.Should().BeFalse();
                adapter.Reply.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new("unauthorized late answer", null)));
                await Task.WhenAll(running, stopping).WaitAsync(TimeSpan.FromSeconds(20), f.Token);
                reader.Inner.Released.Should().BeTrue();
                composed.Turns.IsQuiescent.Should().BeTrue();
                var message = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                message.Body.Should().Be("original retained task");
                message.AssistantResponse.Should().BeNull();
                message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            }
            finally
            {
                adapter.Reply.TrySetResult(new(ModelTurnOutcome.Cancelled, ModelProviderSelection.OllamaCandidate));
                await native.Main.ExitAsync();
            }
        });
    }

    private static string NamedAgentScratch()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static Button NamedAgentButton(AgentRunWindow window, string label) =>
        ((StackPanel)((ScrollViewer)window.Content!).Content!).Children.OfType<Button>()
            .Single(button => Equals(button.Content, label));

    private static ArtifactCatalogue NamedCatalogue(NamedSourceReader reader, string scenario = "success")
    {
        var definition = ArtifactFileDocument.Parse(Encoding.UTF8.GetBytes(reader.Inner.Text), ArtifactKind.Agent,
            "additional", "existing").Definition with { UseControlId = ArtifactUseIdentity.File(reader.Metadata.CanonicalPath) };
        if (scenario is "disabled") { definition = definition with { IsUseDisabled = true }; }
        if (scenario is "missing-name")
        {
            definition = new("disk.additional.agent.other", ArtifactKind.Agent, "Other role", "Other", "other",
                ["other"], "additional", definition.Version, definition.Digest, definition.Content)
            { UseControlId = definition.UseControlId };
        }
        if (scenario is not "duplicate-name") { return new([definition]); }
        var duplicate = new ArtifactDefinition("disk.additional.agent.second", ArtifactKind.Agent, definition.Name,
            "Other source", "second", ["second"], "additional", definition.Version, definition.Digest, definition.Content)
        { UseControlId = ArtifactUseIdentity.File(reader.Metadata.CanonicalPath + ".other") };
        return new([definition, duplicate]);
    }

    private static async Task NamedAgentInput(NativeUxFixtureSession native, string text, bool voice)
    {
        if (!voice)
        {
            native.Main.CommandText = text;
            await native.Main.RunTypedCommand.ExecuteAsync();
            return;
        }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observation = Window.WindowOpenedEvent.AddClassHandler<AgentRunWindow>((window, _) =>
        {
            window.Status.PropertyChanged += (_, args) =>
            {
                if (args.Property == TextBlock.TextProperty && window.Status.Text?.StartsWith("Agent:", StringComparison.Ordinal) == true)
                { completed.TrySetResult(); }
            };
        });
        native.Voice.PublishTranscript(native.Main.AssistantName + ", " + text);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), native.Token);
    }

    private sealed class NamedSourceReader : IAgentProfileReader, IAgentProfileSelection
    {
        internal NativeAgentReader Inner { get; } = new();
        internal bool Missing { get; set; }
        internal int Reads { get; private set; }
        public LocalFileMetadata Metadata => Inner.Metadata;
        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token)
        {
            path.Should().Be(Metadata.CanonicalPath);
            return Task.FromResult<IAgentProfileSelection>(this);
        }
        public Task<byte[]> ReadAsync(CancellationToken token)
        {
            Reads++;
            if (Missing) { throw new FileNotFoundException("Exact source deleted."); }
            return Inner.ReadAsync(token);
        }
        public void Dispose() => Inner.Dispose();
    }

    private sealed class NamedAgentLogger : ILogger<AgentRunController>
    {
        internal List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
