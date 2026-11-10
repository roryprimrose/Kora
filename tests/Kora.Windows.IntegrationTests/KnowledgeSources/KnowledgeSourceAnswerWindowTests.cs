using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Context;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Application.Infrastructure;
using Kora.Application.KnowledgeSources;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Core.Tools;
using Kora.KnowledgeSources;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Context;
using Kora.Windows.KnowledgeSources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.KnowledgeSources;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class KnowledgeSourceAnswerWindowTests
{
    [Theory]
    [InlineData("supported")]
    [InlineData("unsupported")]
    [InlineData("invalid")]
    [InlineData("unqualified")]
    [InlineData("cancelled")]
    [InlineData("disabled")]
    public Task ActualIndexedStoreRestartNativeConsentAndModelHostRetainOnlyTheCanonicalValidatedAnswer(string outcome) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
            {
                using var native = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
                await native.InitializeAsync();
                native.Main.BindClipboardOwnershipGate(static () => true);
                using var originals = new OwnedStorageFixture();
                var root = originals.LocalRoot;
                await File.WriteAllTextAsync(Path.Combine(root, "notes.md"), "alpha original source", native.Token);
                var store = new WindowsKnowledgeSourceStore(native);
                var manager = new KnowledgeSourceManager(new WindowsLocalFileInspector(native), store, native.Audit, TimeProvider.System);
                var saving = new KnowledgeSourcesWindow(native.Main, manager, new SourcePicker(root));
                saving.Show();
                saving.UpdateLayout();
                try
                {
                    saving.GetVisualDescendants().OfType<TextBox>().Single(item =>
                        string.Equals(AutomationProperties.GetName(item), "Knowledge source name", StringComparison.Ordinal)).Text = "Indexed";
                    await Press(saving, "Choose folder and review metadata");
                    await Press(saving, "Confirm read of this exact reviewed membership");
                    saving.GetVisualDescendants().OfType<Button>().Select(button => button.Content)
                        .Should().Contain("Confirm save of this exact complete device-local snapshot",
                            "capture must finish before saving");
                    await Press(saving, "Confirm save of this exact complete device-local snapshot");
                }
                finally { await saving.StopAsync(); manager.Dispose(); }
                store = new WindowsKnowledgeSourceStore(native);
                var source = (await store.ListAsync(native.Token)).Single();
                var revision = await store.ReadAsync(source, native.Token);
                revision.Indexes.Should().ContainSingle();
                using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
                var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(bootstrapper);
                var registry = new ReadOnlyCapabilityRegistry(native.Access, new(), new(), new(new Kora.Application.AssemblyApplicationInfo()),
                    new(bootstrapper), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
                var adapter = new RetainedAdapter(outcome);
                await using var modes = new AudioControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
                var policy = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(native), modes, native.Audit);
                await using var models = new ModelTurnHost(native.Interactions, native.Access, native.Access, registry, native.Audit,
                    NullLogger<ModelTurnHost>.Instance, TimeProvider.System,
                    [new(ModelProviderSelection.OllamaCandidate, outcome is "unqualified" ? ModelQualificationGate.None
                    : ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    DateTimeOffset.UtcNow.AddMinutes(5), adapter)], policy);
                await using var conversation = new SessionConversationService(native.Interactions, native.Interactions, native.Sessions,
                    native.Access, models, TimeProvider.System, NullLogger<SessionConversationService>.Instance, execution: models);
                var legacy = new Fixture();
                using var services = new ServiceCollection()
                    .AddSingleton<ILocalFileRetrieval>(new LocalFileLexicalRetrieval())
                    .AddSingleton<ILocalFileModelUsePolicy>(LocalSourceUsePolicy.Instance)
                    .AddSingleton<IModelContextRunner>(legacy)
                    .AddSingleton<ISecurityAuditLog>(native.Audit)
                    .AddSingleton(TimeProvider.System)
                    .AddSingleton<Microsoft.Extensions.Logging.ILogger<KnowledgeSourceAnswerWorkflow>>(NullLogger<KnowledgeSourceAnswerWorkflow>.Instance)
                    .AddSingleton(native.Sessions).AddSingleton(conversation).BuildServiceProvider();
                manager = new(new WindowsLocalFileInspector(native), store, native.Audit, TimeProvider.System);
                var workflow = ActivatorUtilities.CreateInstance<KnowledgeSourceAnswerWorkflow>(services, manager);
                workflow.RetainsConversation.Should().BeTrue();
                var window = new KnowledgeSourcesWindow(native.Main, manager, answers: workflow);
                window.Show();
                window.UpdateLayout();
                try
                {
                    await Press(window, "Reload source inventory");
                    window.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
                    Question(window).Text = "  What does alpha identify?  ";
                    await Press(window, "Review selected named source excerpts for this question");
                    Status(window).Text.Should().Contain("Retain this exact question").And.Contain("session-history retention");
                    adapter.Calls.Should().Be(0);
                    (await native.Sessions.ReadMetadataAsync(null, 50, native.Token)).Records
                        .Should().NotContain(item => item.Metadata != null
                            && string.Equals(item.Metadata.Name.Value, LocalContextSessionTarget.FreshSessionName, StringComparison.Ordinal));
                    if (outcome is "cancelled" or "disabled")
                    {
                        var answering = Press(window, "Confirm one private local-only no-tools named-source answer");
                        (await Task.WhenAny(adapter.Entered.Task, answering)).Should().BeSameAs(adapter.Entered.Task);
                        var management = outcome is "disabled" ? new KnowledgeSourcesWindow(native.Main, manager) : null;
                        try
                        {
                            Task retirement;
                            if (management is not null)
                            {
                                management.Show();
                                await Press(management, "Reload source inventory");
                                management.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
                                retirement = Press(management, "Disable selected source");
                            }
                            else { retirement = window.StopAsync(); }
                            (await Task.WhenAny(adapter.Cancelled.Task, retirement)).Should().BeSameAs(adapter.Cancelled.Task);
                            retirement.IsCompleted.Should().BeFalse("the real cancellation-ignoring provider must physically join");
                            models.IsQuiescent.Should().BeFalse();
                            adapter.Release.SetResult();
                            await retirement;
                            await answering;
                            using var exclusive = File.Open(Path.Combine(root, "notes.md"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        }
                        finally
                        {
                            adapter.Release.TrySetResult();
                            if (management is not null) { await management.StopAsync(); }
                        }
                    }
                    else { await Press(window, "Confirm one private local-only no-tools named-source answer"); }
                    legacy.Context.Should().BeNull();
                    var target = (await native.Sessions.ReadMetadataAsync(null, 50, native.Token)).Records
                        .Single(item => item.Metadata != null
                            && string.Equals(item.Metadata.Name.Value, LocalContextSessionTarget.FreshSessionName, StringComparison.Ordinal));
                    var retained = (await conversation.ReadPageAsync(target.Authority.SessionId, null, 50, native.Token)).Messages.Single();
                    retained.Body.Should().Be("  What does alpha identify?  ");
                    retained.Request.SessionId.Should().Be(target.Authority.SessionId);
                    if (outcome is "supported" or "unsupported")
                    {
                        retained.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
                        retained.AssistantResponse!.Body.Should().Contain(outcome is "supported"
                            ? "alpha identifies the original source" : "The selected evidence does not support an answer.");
                        Status(window).Text.Should().Contain("validated final answer are retained");
                        if (outcome is "supported")
                        {
                            retained.AssistantResponse.Body.Should().Contain(revision.Reference.RevisionId.ToString("D"))
                                .And.Contain(revision.Files[0].Digest).And.Contain("UTF-16");
                            await Press(window, "Inspect exact answer citation: notes.md, line 1");
                            window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                                string.Equals(AutomationProperties.GetName(item), "Exact admitted untrusted text, never executed",
                                    StringComparison.Ordinal)).SelectedText.Should().Be("alpha original source");
                        }
                    }
                    else
                    {
                        if (outcome is "cancelled" or "disabled") { retained.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded); }
                        else { retained.Outcome.Should().Be(outcome is "unqualified" ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Unknown); }
                        retained.AssistantResponse.Should().BeNull();
                        if (outcome is not ("cancelled" or "disabled"))
                        { Status(window).Text.Should().Contain("No retained supported answer is claimed"); }
                    }
                    adapter.Calls.Should().Be(outcome is "unqualified" ? 0 : 1);
                    models.IsQuiescent.Should().BeTrue();
                    if (outcome is not ("cancelled" or "disabled"))
                    {
                        await Press(window, "Disable selected source");
                        Status(window).Text.Should().BeNull();
                        window.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
                        await Press(window, "Review removal of Kora copies");
                        await Press(window, "Confirm remove this inventoried source and Kora copies");
                        (await store.ListAsync(native.Token)).Should().BeEmpty();
                        (await File.ReadAllTextAsync(Path.Combine(root, "notes.md"), native.Token)).Should().Be("alpha original source");
                    }
                    var readback = (await conversation.ReadPageAsync(target.Authority.SessionId, null, 50, native.Token)).Messages.Single();
                    readback.Should().Be(retained);
                    if (outcome is not "cancelled")
                    {
                        await FluentActions.Awaiting(() => store.ReadAsync(source, native.Token)).Should()
                            .ThrowAsync<InvalidOperationException>()
                            .WithMessage("The exact source inventory changed; reload and review it again.");
                    }
                }
                finally { await window.StopAsync(); manager.Dispose(); legacy.Manager.Dispose(); }
            });
        });

    private sealed class SourcePicker(string root) : IUserFolderPicker
    {
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(root);
    }

    private sealed class RetainedAdapter(string outcome) : IModelTurnAdapter
    {
        internal int Calls;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, Kora.Core.Providers.ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            using var wire = JsonDocument.Parse(context);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("  What does alpha identify?  ");
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            var evidence = wire.RootElement.GetProperty("Evidence").EnumerateArray().ToArray();
            evidence.Should().ContainSingle();
            if (outcome is "cancelled" or "disabled")
            {
                using var cancellation = token.Register(() => Cancelled.TrySetResult());
                Entered.SetResult();
                await Release.Task.ConfigureAwait(false);
            }
            var answer = outcome is "invalid" ? "not JSON" : outcome is "unsupported" ? "{\"unsupported\":true,\"claims\":[]}"
                : JsonSerializer.Serialize(new
                {
                    unsupported = false,
                    claims = new[] { new
                {
                    text = "alpha identifies the original source",
                    evidenceIds = evidence.Select(item => item.GetProperty("Id").GetProperty("Value").GetGuid()),
                } }
                });
            return new ModelProviderReply(ModelTurnOutcome.Succeeded, provenance.Selection, new(answer, null));
        }
    }

    [Fact]
    public Task TwoExplicitRowsHaveOneReviewOneCombinedAnswerAndUnambiguousExactCitationNavigation() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var host = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await host.InitializeAsync();
            host.Main.BindClipboardOwnershipGate(static () => true);
            var f = new Fixture();
            var window = new KnowledgeSourcesWindow(host.Main, f.Manager, answers: f.Workflow);
            window.Show();
            window.UpdateLayout();
            try
            {
                await Press(window, "Reload source inventory");
                var sources = window.GetVisualDescendants().OfType<ListBox>().Single();
                var selected = sources.SelectedItems!;
                selected.Cast<KnowledgeSource>().Should().BeEmpty();
                selected.Add(f.Sources[0]);
                selected.Add(f.Sources[1]);
                Question(window).Text = "Compare alpha in First and Second";
                f.Context.Should().BeNull();
                await Press(window, "Review selected named source excerpts for this question");
                Status(window).Text.Should().Contain("First; exact retained revision").And.Contain("Second; exact retained revision")
                    .And.Contain("alpha FIRST retained").And.Contain("alpha SECOND retained")
                    .And.Contain("Compare alpha in First and Second").And.Contain("2 citations")
                    .And.Contain("Private LocalOnly").And.Contain("no tools");
                f.Context.Should().BeNull();
                await Press(window, "Confirm one private local-only no-tools named-source answer");
                f.Context!.Evidence.Should().HaveCount(2);
                f.Context.Tools.Should().BeEmpty();
                f.Context.UserRequest.Should().Be("Compare alpha in First and Second");
                var reads = f.StoreReads;
                var inspections = f.Inspections;
                await Press(window, "Inspect exact answer citation: First: notes.md, line 1");
                var preview = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), "Exact admitted untrusted text, never executed", StringComparison.Ordinal));
                preview.Text.Should().Be("alpha FIRST retained");
                await Press(window, "Inspect exact answer citation: Second: notes.md, line 1");
                preview.Text.Should().Be("alpha SECOND retained");
                preview.SelectionStart.Should().Be(0);
                preview.SelectionEnd.Should().Be(preview.Text!.Length);
                f.StoreReads.Should().Be(reads);
                f.Inspections.Should().Be(inspections);
                f.OriginalReads.Should().Be(0);
                selected.Remove(f.Sources[0]);
                Status(window).Text.Should().BeNull();
                preview.Text.Should().BeNull();
            }
            finally { await window.StopAsync(); }
            f.Disposals.Should().Be(2);
        });

    [Fact]
    public Task ChosenNamedSourceSectionsAreReviewedSeparatelyAndOnlyThoseSectionsReachTheAnswer() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var host = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await host.InitializeAsync();
            host.Main.BindClipboardOwnershipGate(static () => true);
            var f = new Fixture(multipleFiles: true);
            var window = new KnowledgeSourcesWindow(host.Main, f.Manager, answers: f.Workflow);
            window.Show();
            window.UpdateLayout();
            try
            {
                await Press(window, "Reload source inventory");
                window.GetVisualDescendants().OfType<ListBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), "Explicit named knowledge source selection",
                        StringComparison.Ordinal)).SelectedIndex = 0;
                Question(window).Text = "  alpha question?  ";
                await Press(window, "Review selected named source excerpts for this question");
                var candidates = window.GetVisualDescendants().OfType<ListBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), "Choose whole retrieved named-source excerpts; none selected by default",
                        StringComparison.Ordinal));
                candidates.SelectedItems!.Cast<LocalFileCitation>().Should().BeEmpty();
                candidates.Items.Should().HaveCount(2);
                f.Context.Should().BeNull();
                candidates.SelectedIndex = 1;
                await Press(window, "Review only chosen named-source excerpts");
                Status(window).Text.Should().Contain("User omitted 1 retrieved excerpt").And.Contain("alpha chosen retained")
                    .And.NotContain("alpha FIRST retained").And.Contain("  alpha question?  ");
                f.Context.Should().BeNull();
                candidates.SelectedItems!.Clear();
                window.GetVisualDescendants().OfType<Button>().Should().NotContain(item =>
                    item.Content != null && string.Equals(item.Content.ToString(),
                        "Confirm one private local-only no-tools named-source answer", StringComparison.Ordinal));
                window.GetVisualDescendants().OfType<Button>().Single(item =>
                    string.Equals(item.Content?.ToString(), "Review only chosen named-source excerpts", StringComparison.Ordinal))
                    .IsEnabled.Should().BeFalse();
                candidates.SelectedIndex = 1;
                await Press(window, "Review only chosen named-source excerpts");
                await Press(window, "Confirm one private local-only no-tools named-source answer");
                f.Context!.UserRequest.Should().Be("  alpha question?  ");
                f.Context.Evidence.Should().ContainSingle().Which.Content.Should().Be("alpha chosen retained");
                Status(window).Text.Should().Contain("Succeeded");
                await Press(window, "Inspect exact answer citation: selected.md, line 1");
                var preview = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), "Exact admitted untrusted text, never executed", StringComparison.Ordinal));
                preview.Text.Should().Be("alpha chosen retained");
                f.OriginalReads.Should().Be(0);
            }
            finally { await window.StopAsync(); }
        });

    [Fact]
    public Task NativeReviewSeparateConsentAndCitationUseOnlyExactRetainedSelectedSource() =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var host = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await host.InitializeAsync();
            host.Main.BindClipboardOwnershipGate(static () => true);
            var f = new Fixture();
            var window = new KnowledgeSourcesWindow(host.Main, f.Manager, answers: f.Workflow);
            window.Show();
            window.UpdateLayout();
            try
            {
                await Press(window, "Reload source inventory");
                window.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 1;
                Question(window).Text = "  alpha question?  ";
                f.Context.Should().BeNull();
                await Press(window, "Review selected named source excerpts for this question");
                f.Context.Should().BeNull();
                Status(window).Text.Should().Contain("Second").And.Contain("alpha SECOND retained")
                    .And.Contain("  alpha question?  ").And.Contain("Private LocalOnly");
                await Press(window, "Confirm one private local-only no-tools named-source answer");
                f.Context!.UserRequest.Should().Be("  alpha question?  ");
                f.Context.Evidence.Should().ContainSingle().Which.Content.Should().Be("alpha SECOND retained");
                Status(window).Text.Should().Contain("Succeeded").And.Contain("synthetic supported claim");
                var storeReads = f.StoreReads;
                var inspections = f.Inspections;
                await Press(window, "Inspect exact answer citation: notes.md, line 1");
                f.StoreReads.Should().Be(storeReads);
                f.Inspections.Should().Be(inspections);
                f.OriginalReads.Should().Be(0);
                var preview = window.GetVisualDescendants().OfType<TextBox>().Single(item =>
                    string.Equals(AutomationProperties.GetName(item), "Exact admitted untrusted text, never executed", StringComparison.Ordinal));
                preview.Text.Should().Be("alpha SECOND retained");
                preview.SelectionStart.Should().Be(0);
                preview.SelectionEnd.Should().Be(preview.Text!.Length);
                Question(window).Text = "new question";
                Status(window).Text.Should().BeNull();
                window.GetVisualDescendants().OfType<Button>().Should().NotContain(item =>
                    item.Content != null && string.Equals(item.Content.ToString(), "Confirm one private local-only no-tools named-source answer", StringComparison.Ordinal));
            }
            finally { await window.StopAsync(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OriginalTypedOrVoiceEntryChoosesExactNameButNeverAutoConfirmsAndMissingNameRequiresExplicitSelection(bool voice) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var host = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await host.InitializeAsync();
            host.Main.BindClipboardOwnershipGate(static () => true);
            var f = new Fixture();
            var window = new KnowledgeSourcesWindow(host.Main, f.Manager, answers: f.Workflow);
            window.Show();
            window.UpdateLayout();
            try
            {
                using var input = HostActivity.BeginRoot(HostRequest.Create(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi),
                    HostActivityLayer.Application, HostOperation.Request);
                await window.OpenAnswerAsync(new("Second", "  alpha?  "), () => true);
                Question(window).Text.Should().Be("  alpha?  ");
                window.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem.Should().Be(f.Sources[1]);
                f.Inspections.Should().Be(0);
                await Press(window, "Review selected named source excerpts for this question");
                f.Context.Should().BeNull();
                await window.OpenAnswerAsync(new("Ambiguous or missing", "alpha?"), () => true);
                window.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem.Should().BeNull();
                Status(window).Text.Should().Contain("Explicitly select");
                f.Context.Should().BeNull();
            }
            finally { await window.StopAsync(); }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingPrivateSurfaceCancelsAndJoinsIgnoringModelBeforeDisposalWithoutLateResult(bool chosen) =>
        ClosingSourceSurface(chosen, hosted: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HostedPrivateSourceSurfaceJoinsIgnoringModelAndSuppressesLateAnswer(bool chosen) =>
        ClosingSourceSurface(chosen, hosted: true);

    private static Task ClosingSourceSurface(bool chosen, bool hosted) =>
        HeadlessSession.RunAsync(async () =>
        {
            using var listener = Listen();
            using var host = new NativeUxFixtureSession(OwnedStorageFixture.ScratchParent());
            await host.InitializeAsync();
            host.Main.BindClipboardOwnershipGate(static () => true);
            var f = new Fixture(multipleFiles: chosen) { Delay = true };
            using var responses = hosted ? new ResponseWindowController(host.Main,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<ResponseWindowController>.Instance) : null;
            var window = responses is not null ? new KnowledgeSourcesView(responses.PrepareWorkbenchOwner(), host.Main, f.Manager, answers: f.Workflow)
                : new KnowledgeSourcesWindow(host.Main, f.Manager, answers: f.Workflow).View;
            var windows = new List<Window>();
            using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((view, _) => windows.Add(view));
            window.Show();
            window.UpdateLayout();
            try
            {
                await Press(window, "Reload source inventory");
                window.GetVisualDescendants().OfType<ListBox>().Single().SelectedIndex = 0;
                Question(window).Text = "alpha";
                await Press(window, "Review selected named source excerpts for this question");
                if (chosen)
                {
                    window.GetVisualDescendants().OfType<ListBox>().Single(item =>
                        string.Equals(AutomationProperties.GetName(item), "Choose whole retrieved named-source excerpts; none selected by default",
                            StringComparison.Ordinal)).SelectedIndex = 1;
                    await Press(window, "Review only chosen named-source excerpts");
                }
                var work = Press(window, "Confirm one private local-only no-tools named-source answer");
                await f.Entered.Task.WaitAsync(host.Token);
                var status = Status(window);
                if (hosted) { window.Owner.Hide(); }
                var stop = window.StopAsync();
                await f.Cancelled.Task.WaitAsync(host.Token);
                stop.IsCompleted.Should().BeFalse();
                window.Owner.IsVisible.Should().Be(!hosted);
                f.Disposals.Should().Be(0);
                f.Release.SetResult();
                await work;
                await stop;
                window.Content.Should().BeNull();
                f.Disposals.Should().Be(1);
                status.Text.Should().BeNull();
                if (hosted) { windows.Distinct().Should().Equal(window.Owner); }
            }
            finally { f.Release.TrySetResult(); await window.StopAsync(); }
        });

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
    private static TextBox Question(Control window) => window.GetVisualDescendants().OfType<TextBox>().Single(item =>
        string.Equals(AutomationProperties.GetName(item), "New named-source answer question", StringComparison.Ordinal));
    private static TextBlock Status(Control window) => window.GetVisualDescendants().OfType<TextBlock>().Single(item =>
        string.Equals(AutomationProperties.GetName(item), "Named-source answer review and outcome", StringComparison.Ordinal));
    private static async Task Press(Control window, string caption)
    {
        window.UpdateLayout();
        var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
        buttons.Should().ContainSingle(item => string.Equals(item.Content as string, caption, StringComparison.Ordinal),
            "the exact operation must be available; presentation: {0}",
            string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text)));
        await ((AsyncCommand)buttons.Single(item => string.Equals(item.Content?.ToString(), caption, StringComparison.Ordinal)).Command!).ExecuteAsync();
        window.UpdateLayout();
    }

    private sealed class Fixture : IKnowledgeSourceInspector, IKnowledgeSourceStore, IModelContextRunner, ISecurityAuditLog
    {
        internal KnowledgeSource[] Sources { get; }
        private readonly KnowledgeSourceRevision[] revisions;
        internal KnowledgeSourceManager Manager { get; }
        internal KnowledgeSourceAnswerWorkflow Workflow { get; }
        internal ModelContextEnvelope? Context;
        internal int StoreReads;
        internal int Inspections;
        internal int OriginalReads;
        internal int Disposals;
        internal bool Delay;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Fixture(bool multipleFiles = false)
        {
            Sources = Enumerable.Range(0, 2).Select(index =>
            {
                var path = index == 0 ? @"C:\First" : @"C:\Second";
                var text = index == 0 ? "alpha FIRST retained" : "alpha SECOND retained";
                LocalFileMetadata[] files = multipleFiles
                    ? [new(path + @"\notes.md", "file", Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch),
                        new(path + @"\selected.md", "selected", Encoding.UTF8.GetByteCount("alpha chosen retained"), DateTimeOffset.UnixEpoch)]
                    : [new(path + @"\notes.md", "file", Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch)];
                var metadata = new KnowledgeSourceMetadata(path, "root", KnowledgeSourceScope.ImmediateFiles,
                    [new(path, "root")], files);
                return new KnowledgeSource(Guid.NewGuid(), 2, index == 0 ? "First" : "Second", metadata,
                    KnowledgeSourceState.Enabled, DateTimeOffset.UnixEpoch, 100);
            }).ToArray();
            revisions = Sources.Select((source, index) =>
            {
                var review = new KnowledgeSourceReview(Guid.NewGuid(), source.Id, HostRequest.Create(RequestOrigin.LocalUi), source.Folder);
                return new KnowledgeSourceRevision(review, source.Folder.Files.Select((file, fileIndex) => new LocalFileRevision(
                    new(review.ReviewId, review.SourceId, review.Request, file),
                    Encoding.UTF8.GetBytes(fileIndex == 1 ? "alpha chosen retained"
                        : index == 0 ? "alpha FIRST retained" : "alpha SECOND retained"), DateTimeOffset.UnixEpoch)).ToArray());
            }).ToArray();
            Manager = new(this, this, this, TimeProvider.System);
            Workflow = new(Manager, new LocalFileLexicalRetrieval(), LocalSourceUsePolicy.Instance, this, this,
                TimeProvider.System, NullLogger<KnowledgeSourceAnswerWorkflow>.Instance);
        }
        public Task<IKnowledgeSourceSelection> InspectSourceAsync(string selectedPath, KnowledgeSourceScope scope, CancellationToken cancellationToken)
        {
            Inspections++;
            return Task.FromResult<IKnowledgeSourceSelection>(new Selection(this, Sources.Single(item =>
                string.Equals(item.Folder.CanonicalPath, selectedPath, StringComparison.Ordinal)).Folder));
        }
        private sealed class Selection(Fixture owner, KnowledgeSourceMetadata metadata) : IKnowledgeSourceSelection
        {
            public KnowledgeSourceMetadata Metadata => metadata;
            public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken)
            { owner.OriginalReads++; throw new NotSupportedException(); }
            public void Dispose() => owner.Disposals++;
        }
        public Task<IReadOnlyList<KnowledgeSource>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeSource>>(Sources);
        public Task<KnowledgeSourceRevision> ReadAsync(KnowledgeSource exact, CancellationToken cancellationToken)
        {
            StoreReads++;
            return Task.FromResult(revisions.Single(item => item.Reference.SourceId == exact.Id));
        }
        public Task<KnowledgeSource> BeginRefreshAsync(KnowledgeSource exact, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KnowledgeSource> PublishAsync(KnowledgeSource? expected, string name, KnowledgeSourceRevision snapshot,
            IReadOnlyList<ReadOnlyMemory<byte>> originalBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DisableAsync(KnowledgeSource exact, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveAsync(KnowledgeSource exact, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ModelContextAdmission> AdmitAsync(LocalContextSessionTarget target, Func<bool> sourceCurrent, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null) => Task.FromResult(new ModelContextAdmission(new Turn(this, target.Request)));
        private sealed class Turn(Fixture owner, HostRequest request) : IAdmittedModelContextTurn
        {
            public HostRequest Request => request;
            public HostRevision SessionGeneration => new(1);
            public Task<LocalContextTurnReceipt> Completion { get; private set; } =
                Task.FromResult(new LocalContextTurnReceipt(request, ModelTurnOutcome.Cancelled, LocalContextTurnReason.Cancelled));
            public Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, CancellationToken token) => Completion = owner.Run(context, token);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private async Task<LocalContextTurnReceipt> Run(ModelContextEnvelope context, CancellationToken token)
        {
            Context = context;
            using var registration = token.Register(() => Cancelled.TrySetResult());
            Entered.TrySetResult();
            if (Delay) { await Release.Task; }
            var model = new ModelTurnResult(ModelTurnOutcome.Succeeded, ModelTurnReason.None,
                new(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate),
                new(JsonSerializer.Serialize(new
                {
                    unsupported = false,
                    claims = new[] { new { text = "synthetic supported claim",
                    evidenceIds = context.Evidence.Select(item => item.Id.Value) } }
                }), null));
            return new(context.Request, ModelTurnOutcome.Succeeded, LocalContextTurnReason.Completed, HostTaskState.Succeeded, model);
        }
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}