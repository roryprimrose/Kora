using System.Diagnostics;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Context;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.KnowledgeSources;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Kora.KnowledgeSources;
using Kora.NativeUxFixture;
using Kora.Windows.Context;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.KnowledgeSources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class SessionKnowledgeSourceAnswerTests
{
    [Theory]
    [InlineData("supported")]
    [InlineData("closed")]
    [InlineData("privacy")]
    public Task ActualNativeBindReopenIndependentQuestionAndCanonicalAnswerKeepTheOriginalConversation(string outcome) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            };
            ActivitySource.AddActivityListener(listener);
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                await using var native = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
                await native.InitializeAsync();
                native.Main.BindClipboardOwnershipGate(static () => true);
                using var originals = new OwnedStorageFixture();
                var file = Path.Combine(originals.LocalRoot, "notes.md");
                await File.WriteAllTextAsync(file, "alpha original source", native.Token);
                var store = new WindowsKnowledgeSourceStore(native);
                using (var registration = new KnowledgeSourceManager(new WindowsLocalFileInspector(native), store, native.Audit, native.Time))
                {
                    var review = await registration.ReviewAsync("Bound team notes", originals.LocalRoot, null, () => true, native.Token);
                    var captured = await registration.CaptureAsync(review.ReviewId, () => true, native.Token);
                    await registration.PublishAsync(captured, () => true, native.Token);
                }
                var source = (await store.ListAsync(native.Token)).Single();
                var revision = await store.ReadAsync(source, native.Token);
                using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
                var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
                var registry = new ReadOnlyCapabilityRegistry(native.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                    new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
                var cancel = outcome is not "supported";
                var adapter = new Adapter(cancel);
                await using var admission = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
                var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(native), admission, native.Audit);
                await using var models = new ModelTurnHost(native.Interactions, native.Access, native.Access, registry, native.Audit,
                    NullLogger<ModelTurnHost>.Instance, native.Time,
                    [new(ModelProviderSelection.OllamaCandidate, ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                        native.Time.GetUtcNow().AddMinutes(5), adapter)], policy);
                await using var conversation = new SessionConversationService(native.Interactions, native.Interactions, native.Sessions,
                    native.Access, models, native.Time, NullLogger<SessionConversationService>.Instance, execution: models);
                var target = await native.Sessions.CreateAsync(new("Original source conversation"), RequestOrigin.LocalUi, native.Token);
                var originalSessionCount = (await native.Sessions.ReadMetadataAsync(null, 50, native.Token)).Records.Length;
                var retention = await native.Interactions.ReadRetentionAsync(target.Authority.SessionId, native.Token);
                var legacy = new ForbiddenLegacyRunner();
                SessionKnowledgeSourceWorkflow CreateWorkflow()
                {
                    var manager = new KnowledgeSourceManager(new WindowsLocalFileInspector(native), store, native.Audit, native.Time);
                    var answers = new KnowledgeSourceAnswerWorkflow(manager, new LocalFileLexicalRetrieval(), LocalSourceUsePolicy.Instance,
                        legacy, native.Audit, native.Time, NullLogger<KnowledgeSourceAnswerWorkflow>.Instance, native.Sessions, conversation);
                    return new(native.Sessions, manager, store, answers, native.Time);
                }
                var owner = new Window { Title = "Original conversation owner" };
                owner.Show();
                var controller = new SessionKnowledgeSourceController(native.Main, () => owner, CreateWorkflow);
                var next = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
                {
                    if (string.Equals(window.Title, "This conversation's documentation source", StringComparison.Ordinal))
                    { next.TrySetResult(window); }
                });
                var opening = controller.OpenAsync(target, () => true, native.Token);
                try
                {
                    var window = await WaitForWindow(next.Task, opening, native.Token);
                    var selection = window.GetVisualDescendants().OfType<ComboBox>().Single();
                    selection.SelectedIndex.Should().Be(-1);
                    selection.SelectedIndex = 0;
                    await Press(window, "Open selected existing revision and review association");
                    adapter.Calls.Should().Be(0);
                    (await native.Interactions.ReadBinding(target.Authority.SessionId, native.Token)).Should().BeNull();
                    await Press(window, "Confirm this exact metadata-only association");
                    var binding = (await native.Interactions.ReadBinding(target.Authority.SessionId, native.Token))!;
                    binding.Source.Should().Be(revision.Reference);
                    (await native.Interactions.ReadRetentionAsync(target.Authority.SessionId, native.Token)).Should().Be(retention);
                    controller.Close();
                    await opening;
                    next = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    opening = controller.OpenAsync(target, () => true, native.Token);
                    window = await WaitForWindow(next.Task, opening, native.Token);
                    Status(window).Text.Should().Contain("metadata reopened").And.Contain("No source body");
                    using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    adapter.Calls.Should().Be(0);
                    await Press(window, "Explicitly open associated source with current access and integrity checks");
                    window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                        string.Equals(AutomationProperties.GetName(item), "New question for this conversation's associated source",
                            StringComparison.Ordinal)).Text = "  What does alpha identify?  ";
                    await Press(window, "Review question, exact excerpts and original conversation target");
                    adapter.Calls.Should().Be(0);
                    var answering = Press(window, "Confirm one LocalOnly toolsNONE answer retained in this original conversation");
                    if (cancel)
                    {
                        await adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30), native.Token);
                        if (outcome is "privacy") { native.Access.SetLocked(); }
                        else { controller.Close(); }
                        await adapter.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30), native.Token);
                        opening.IsCompleted.Should().BeFalse();
                        models.IsQuiescent.Should().BeFalse();
                        adapter.Release.SetResult();
                    }
                    await answering;
                    if (cancel)
                    {
                        await opening;
                        window.Content.Should().BeNull();
                        if (outcome is "privacy") { native.Access.SetOpen(true); }
                    }
                    var message = (await conversation.ReadPageAsync(target.Authority.SessionId, null, 50, native.Token)).Messages.Single();
                    message.Request.SessionId.Should().Be(target.Authority.SessionId);
                    message.Body.Should().Be("  What does alpha identify?  ");
                    if (cancel)
                    {
                        await opening;
                        message.AssistantResponse.Should().BeNull();
                        message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
                    }
                    else
                    {
                        message.AssistantResponse!.Body.Should().Contain("alpha identifies the original source")
                            .And.Contain(revision.Reference.RevisionId.ToString("D")).And.Contain(revision.Files[0].Digest);
                        var citation = window.GetVisualDescendants().OfType<Button>().Single(item =>
                            item.Content is string text && text.StartsWith("Inspect retained citation:", StringComparison.Ordinal));
                        await ((AsyncCommand)citation.Command!).ExecuteAsync();
                        window.GetVisualDescendants().OfType<TextBox>().Single(item => item.IsReadOnly)
                            .SelectedText.Should().Be("alpha original source");
                        await Press(window, "Review detach association only");
                        await Press(window, "Confirm detach this exact association only");
                        (await native.Interactions.ReadBinding(target.Authority.SessionId, native.Token)).Should().BeNull();
                        (await store.ReadAsync(source, native.Token)).Reference.Should().Be(revision.Reference);
                        (await conversation.ReadPageAsync(target.Authority.SessionId, null, 50, native.Token)).Messages.Single()
                            .Should().Be(message);
                    }
                    (await native.Sessions.ReadMetadataAsync(null, 50, native.Token)).Records.Length.Should().Be(originalSessionCount);
                    adapter.Calls.Should().Be(1);
                    models.IsQuiescent.Should().BeTrue();
                    (await File.ReadAllTextAsync(file, native.Token)).Should().Be("alpha original source");
                }
                finally
                {
                    adapter.Release.TrySetResult();
                    controller.Close();
                    await opening;
                    owner.Close();
                }
            });
        });

    private static TextBlock Status(Control window) => window.GetVisualDescendants().OfType<TextBlock>().Single(item =>
        string.Equals(AutomationProperties.GetName(item), "Conversation source review and exact outcome", StringComparison.Ordinal));

    private static async Task<Window> WaitForWindow(Task<Window> shown, Task operation, CancellationToken token)
    {
        await Task.WhenAny(shown, operation).WaitAsync(TimeSpan.FromSeconds(30), token);
        if (operation.IsCompleted)
        {
            await operation;
            throw new InvalidOperationException("The source operation ended before its native review opened.");
        }
        return await shown.WaitAsync(TimeSpan.FromSeconds(30), token);
    }

    private static async Task Press(Control window, string label)
    {
        window.UpdateLayout();
        var button = window.GetVisualDescendants().OfType<Button>().Single(item =>
            string.Equals(item.Content as string, label, StringComparison.Ordinal));
        await ((AsyncCommand)button.Command!).ExecuteAsync();
        window.UpdateLayout();
    }

    private sealed class ForbiddenLegacyRunner : IModelContextRunner
    {
        public Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent,
            CancellationToken token, Func<CancellationToken, Task>? validateSource = null) =>
            throw new InvalidOperationException("Bound answers must use the canonical retained-conversation service.");
    }

    private sealed class Adapter(bool delayed) : IModelTurnAdapter
    {
        internal int Calls;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++;
            using var wire = JsonDocument.Parse(context);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("  What does alpha identify?  ");
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().ToArray();
            evidence.Should().ContainSingle();
            if (delayed)
            {
                using var cancelled = token.Register(() => Cancelled.TrySetResult());
                Entered.SetResult();
                await Release.Task.ConfigureAwait(false);
            }
            return new(ModelTurnOutcome.Succeeded, provenance.Selection, new(JsonSerializer.Serialize(new
            {
                unsupported = false,
                claims = new[] { new { text = "alpha identifies the original source",
                    evidenceIds = evidence.Select(item => item.GetProperty("Id").GetProperty("Value").GetGuid()) } },
            }), null));
        }
    }
}
