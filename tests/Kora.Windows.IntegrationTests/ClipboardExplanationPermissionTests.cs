using System.Text;
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
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Tools.Clipboard;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ClipboardExplanationPermissionTests
{
    [Theory]
    [InlineData("once", 4)]
    [InlineData("session", 2)]
    [InlineData("perpetual", 2)]
    [InlineData("deny", 1)]
    public async Task NativeReviewAndActualGrantReachRealHostAndOnlyVolatileExactAnswer(string scope, int expectedQuestions)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            await fixture.Tasks.InitializeAsync(fixture.Token);
            await fixture.Store.InitializeAsync(fixture.Token);
            var controls = new Controls();
            var adapter = new Adapter();
            using var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtimes = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(controls, new(), new(), new(controls), new(dependencies),
                new(runtimes), new(runtimes), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            await using var preferences = new AudioControlAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
            var configuration = new ProviderModeConfigurationService(new Preferences(), preferences, controls);
            await using var modelHost = new ModelTurnHost(fixture.Store, controls, controls, registry, controls,
                NullLogger<ModelTurnHost>.Instance, fixture.Time,
                [new(ModelProviderSelection.OllamaCandidate,
                    ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
                    fixture.Time.GetUtcNow().AddHours(1), adapter)], configuration);
            await using var admission = new ClipboardExplanationAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
            var policy = new ClipboardPermissionSourceUsePolicy(fixture.Store, fixture.Store, fixture.Time);
            var reader = new Reader();
            using var broker = new ClipboardSnapshotBroker(reader, fixture.Time, NullLogger<ClipboardSnapshotBroker>.Instance);
            using var workflow = new ClipboardExplanationWorkflow(broker, new(new ClipboardReadWorkflow(broker, admission, policy)), admission, modelHost, controls,
                fixture.Time, NullLogger<ClipboardExplanationWorkflow>.Instance, policy);
            using var native = new NativeQuestionHost(fixture.Store, fixture.Time, NullLogger<NativeQuestionViewModel>.Instance);
            native.BindGate(() => true);
            native.BindCallObservation(() => new(1, CallState.Clear, false, CallAwareSettings.Default));
            native.BindAdmissionRevision(() => 1);
            native.BindPrivateRevision(() => 1);
            var questions = 0;
            policy.BindPresenter(async (proposal, generation, current, token) =>
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    native.RequestClipboardPermissionAsync(proposal, generation, current, async state =>
                {
                    var question = new QuestionWindow(state, new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance));
                    question.Show();
                    try
                    {
                        questions++;
                        if (string.Equals(proposal.Binding.ActionId, ClipboardPermissionSourceUsePolicy.ReadActionId, StringComparison.Ordinal))
                        {
                            reader.Calls.Should().Be(scope is "once" ? questions / 2 : 0);
                            state.Question.Spec.Text.Should().Contain("without reading the clipboard");
                            var key = state.Key;
                            var grants = await fixture.Store.ReadGrantsAsync(fixture.Token);
                            var retention = await fixture.Store.ReadRetentionAsync(proposal.Request.SessionId, fixture.Token);
#pragma warning disable VSTHRD003 // Join the exact window activation read before simulating the next enabled user gesture.
                            await state.OperationQuiescence;
#pragma warning restore VSTHRD003
                            await state.ExplainPolicyAsync();
                            state.SupportsPolicyExplanation.Should().BeTrue();
                            state.HasPolicyExplanation.Should().BeTrue("native status: {0}", state.Status);
                            state.PolicyExplanationText.Should().Contain("clipboard.read")
                                .And.Contain("Review all").And.Contain("independently of the session level")
                                .And.Contain("NOT supplied").And.NotContain("explicit-native-read");
                            question.FindControl<TextBox>("PolicyExplanation")!.Text.Should().Be(state.PolicyExplanationText);
                            state.ReviewText.Should().BeEmpty();
                            state.CanSubmit.Should().BeFalse();
                            state.Completion.IsCompleted.Should().BeFalse();
                            state.Key.Should().Be(key);
                            reader.Calls.Should().Be(scope is "once" ? questions / 2 : 0);
                            adapter.Calls.Should().Be(scope is "once" ? questions / 2 : 0);
                            (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Equal(grants);
                            (await fixture.Store.ReadRetentionAsync(proposal.Request.SessionId, fixture.Token)).Should().Be(retention);
                            await state.ReturnToApprovalAsync();
                            state.HasPolicyExplanation.Should().BeFalse();
                            state.Key.Should().Be(key);
                            state.IsEditable.Should().BeTrue();
                            state.CanSubmit.Should().BeFalse("reading policy never completes the separate exact record review");
                            state.Completion.IsCompleted.Should().BeFalse();
                            (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Equal(grants);
                        }
                        state.IsApproval.Should().BeTrue();
                        state.CanAnswerByVoice.Should().BeFalse();
                        state.Answer.Choices.Should().BeEmpty();
                        state.Question.Spec.Text.Should().Contain("responsible").And.Contain("does not classify or scan");
                        if (scope is "deny") { await state.CancelAsync(); }
                        else
                        {
                            state.Edit(new([scope]));
                            await state.ReviewAsync();
                            state.CanSubmit.Should().BeTrue();
                            await state.SubmitAsync();
                        }
                        state.Completion.IsCompleted.Should().BeTrue();
                    }
                    finally { question.Close(); }
                }, token)));
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Request);
            var runs = scope is "deny" ? 1 : 2;
            for (var index = 0; index < runs; index++)
            {
                var captured = await workflow.ExplainAsync(null, () => true, fixture.Token);
                if (scope is "deny")
                {
                    captured.Outcome.Should().Be(ClipboardExplanationOutcome.Unavailable);
                    captured.CaptureOutcome.Should().Be(ClipboardOutcome.Denied);
                    broker.Current.Should().BeNull();
                    break;
                }
                captured.Outcome.Should().Be(ClipboardExplanationOutcome.ReviewRequired);
                var source = broker.Current!;
                var window = new ClipboardPreviewWindow(_ => throw new InvalidOperationException("Unexpected clipboard UI failure"));
                ClipboardExplanationResult? result = null;
                window.Show(source, () => Task.CompletedTask, () => Task.CompletedTask,
                    () => ReferenceEquals(broker.Current, source), async () =>
                    {
                        result = await workflow.ExplainAsync(source.SnapshotId, () => true, fixture.Token);
                        window.SetExplanation(workflow.CurrentExplanation);
                    });
                try
                {
                    var consent = window.GetVisualDescendants().OfType<CheckBox>().Single(control =>
                        AutomationProperties.GetName(control) is "Admit this exact clipboard snapshot to local-only inference");
                    adapter.Calls.Should().Be(index);
                    consent.IsChecked = true;
                    var explain = window.GetVisualDescendants().OfType<Button>()
                        .Single(button => Equals(button.Content, "Review and explain locally"));
                    await ((AsyncCommand)explain.Command!).ExecuteAsync();
                    consent.IsChecked.Should().BeFalse();
                    result!.Outcome.Should().Be(scope is "deny"
                        ? ClipboardExplanationOutcome.SourceUseDenied : ClipboardExplanationOutcome.Succeeded,
                        "model reason {0}", result.ModelReason);
                    var answer = window.GetVisualDescendants().OfType<Kora.Controls.NamedTextBlock>().Single(control =>
                        AutomationProperties.GetName(control) is "Volatile local clipboard explanation; untrusted model response");
                    answer.Text.Should().Be(scope is "deny" ? null : Adapter.Answer);
                    if (adapter.Request is { } request)
                    {
                        var recorded = await fixture.Store.ReadTaskAsync(request.SessionId, request.TaskId, fixture.Token);
                        recorded!.Task.State.Should().Be(HostTaskState.Succeeded);
                        recorded.CurrentSource.Should().BeTrue();
                    }
                    window.ClearAndClose();
                    answer.Text.Should().BeNull();
                }
                finally { window.ClearAndClose(); broker.Clear(); }
            }
            questions.Should().Be(expectedQuestions);
            adapter.Calls.Should().Be(scope is "deny" ? 0 : runs);
            reader.Calls.Should().Be(scope is "deny" ? 0 : runs);
            (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().HaveCount(scope is "deny" ? 0 : expectedQuestions);
            fixture.Count("conversation_messages").Should().Be(0);
            fixture.Count("conversation_drafts").Should().Be(0);
            await workflow.WaitForQuiescenceAsync();
            workflow.IsQuiescent.Should().BeTrue();
            foreach (var file in Directory.EnumerateFiles(fixture.Paths.LocalRoot, "*", SearchOption.AllDirectories))
            {
                Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, fixture.Token))
                    .Should().NotContain(Reader.Source).And.NotContain(Adapter.Answer);
            }
        });
    }

    private sealed class Reader : IPlainTextClipboardReader
    {
        internal const string Source = "private-clipboard-fixture-80f7 ordinary untrusted exact source";
        internal int Calls { get; private set; }
        public Task<ClipboardReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ClipboardReadResult(ClipboardOutcome.Captured, Source, 19));
        }
    }

    private sealed class Adapter : IModelTurnAdapter
    {
        internal const string Answer = "Actual synthetic clipboard explanation from the qualified fake adapter.";
        internal int Calls { get; private set; }
        internal HostRequest? Request { get; private set; }
        public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken cancellationToken)
        {
            Calls++;
            Request = provenance.Request;
            using var json = JsonDocument.Parse(context);
            json.RootElement.GetProperty("UserRequest").GetString().Should().Be("Explain the exact reviewed clipboard snapshot.");
            json.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(Reader.Source);
            json.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Succeeded,
                ModelProviderSelection.OllamaCandidate, new(Answer, null)));
        }
    }

    private sealed class Controls : ISessionWorkspaceAccess, ICapabilityHostAccess, ISecurityAuditLog, IApplicationInfo
    {
        public bool CanInspect => true;
        public bool CanControl => true;
        public long ControlRevision => 1;
        public bool IsCurrentHost => true;
        public string Version => "1.0";
        public void Write(SecurityAuditEvent auditEvent) { }
    }

    private sealed class Preferences : IModelProviderModePreferences
    {
        public ModelProviderMode? Load() => null;
        public ModelProviderMode? ReadBack() => null;
        public void BeginWrite() => throw new NotSupportedException();
        public void Save(ModelProviderMode mode) => throw new NotSupportedException();
        public void ConfirmWrite() => throw new NotSupportedException();
    }
}
