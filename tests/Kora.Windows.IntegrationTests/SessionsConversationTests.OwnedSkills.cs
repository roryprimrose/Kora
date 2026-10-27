using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AwesomeAssertions;
using Kora.Application.Skills;
using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Skills;
using Kora.Core.Tools;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionsConversationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedSkillNativeEditorSelectionAndSeparateUseCompleteInExactConversation(bool editAfterPreview)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description", "Literal guidance\nowned skill invoke");
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var useWorkflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time);
            var use = new OwnedSkillInvocationController(native.Main, useWorkflow, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance);
            use.Bind();
            await using var editor = new OwnedSkillEditorController(native.Main, new(store, admission, native.Audit), admission,
                static () => true, static () => 1, NullLogger<OwnedSkillEditorController>.Instance, use);
            editor.Bind();
            OwnedSkillEditorWindow? editing = null;
            OwnedSkillInvocationWindow? invocation = null;
            using var editorOpened = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillEditorWindow>((opened, _) => editing = opened);
            using var useOpened = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillInvocationWindow>((opened, _) => invocation = opened);
            async Task Command(string text) { native.Main.CommandText = text; await native.Main.RunTypedCommand.ExecuteAsync(); }
            await Command("owned skill list");
            var authoring = editing ?? throw new InvalidOperationException("No owned editor.");
            authoring.Saved.SelectedIndex = 0;
            await Command("owned skill reopen example 1.0.0");
            authoring.Guidance.Text.Should().Be(document.Guidance);
            adapter.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
            await Command("owned skill use example 1.0.0");
            var opened = invocation ?? throw new InvalidOperationException("No owned use review.");
            opened.Instructions.Text.Should().Contain(document.ManifestText).And.Contain(document.Guidance);
            opened.Confirm.IsEnabled.Should().BeFalse();
            await Command("owned skill enable");
            await Command("owned skill targets");
            opened.Targets.SelectedIndex = 0;
            const string body = "  unchanged original prose\r\n e\u0301  ";
            await Command("owned skill request: " + body);
            await Command("owned skill review-use");
            opened.Confirm.IsEnabled.Should().BeTrue();
            using (var preview = JsonDocument.Parse(opened.Confirmation.Text![(opened.Confirmation.Text!.IndexOf('{'))..]))
            {
                preview.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
                preview.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(2);
                preview.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            }
            adapter.Calls.Should().Be(0);
            if (editAfterPreview)
            {
                opened.Request.Text = "changed after review";
                opened.Confirm.IsEnabled.Should().BeFalse();
                await Command("owned skill invoke");
                adapter.Calls.Should().Be(0);
                f.Count("conversation_messages").Should().Be(0);
            }
            else
            {
                opened.Confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await use.ActiveOperation;
                adapter.Calls.Should().Be(1);
                opened.Status.Text.Should().Contain("Succeeded").And.Contain("earlier ASSISTANT");
                var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
                retained.Body.Should().Be(body);
                retained.AssistantResponse!.Body.Should().Be("earlier ASSISTANT");
                using var wire = JsonDocument.Parse(adapter.Wire!);
                wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(document.ManifestText);
                wire.RootElement.GetProperty("Evidence")[1].GetProperty("Content").GetString().Should().Be(document.Guidance);
                wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            }
            await Command("owned skill close-use");
            opened.Instructions.Text.Should().BeEmpty();
            opened.Request.Text.Should().BeEmpty();
            opened.Confirmation.Text.Should().BeEmpty();
            (await store.Read("example", "1.0.0", native.Token))!.Document.IsAvailableForInvocation.Should().BeFalse();
            (await editor.PrepareClose()).Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("replacement")]
    [InlineData("missing")]
    [InlineData("parameter-bytes")]
    [InlineData("parameter-replacement")]
    [InlineData("parameter-missing")]
    public async Task OwnedSkillRealPrivatePackageMutationRetiresReviewBeforeModelAdmission(string fault)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new HistoryAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            using var paths = new OwnedStorageFixture();
            var store = new WindowsOwnedDeclarativeSkillStore(paths);
            var parameterized = fault.StartsWith("parameter-", StringComparison.Ordinal);
            if (parameterized) { fault = fault["parameter-".Length..]; }
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description",
                parameterized ? "Review ${input:topic}" : "Original guidance",
                guidanceFormat: parameterized ? OwnedSkillGuidanceFormat.RequiredLiteralInputs : OwnedSkillGuidanceFormat.Literal);
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), f.Token);
            await using var admission = new OwnedSkillControlAdmission(f.Store, f.Store, new(f.Tasks));
            var workflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store,
                composed.Workspace, composed.Conversation, composed.Audit, f.Time);
            var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, static () => true);
            using var review = await workflow.ReviewAsync("example", "1.0.0", authority, static () => true, f.Token);
            var source = Path.Combine(store.Destination(document), "instructions.md");
            if (fault is "bytes") { await File.WriteAllTextAsync(source, "Changed guidance", f.Token); }
            else
            {
                File.Delete(source);
                if (fault is "replacement") { await File.WriteAllBytesAsync(source, document.InstructionBytes.ToArray(), f.Token); }
            }
            await workflow.Invoking(flow => flow.EnableAsync(review, authority, static () => true, f.Token))
                .Should().ThrowAsync<Exception>();
            review.IsCurrent.Should().BeFalse();
            adapter.Calls.Should().Be(0);
            f.Count("conversation_messages").Should().Be(0);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OwnedSkillNativeCloseClearsPrivateBuffersButJoinsIgnoringModelBeforeRelease(bool parameterized, bool privacy)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new InteractionStorageFixture();
            await f.InitializeAsync();
            await WindowsSqliteConversationTests.FinishInitial(f);
            var adapter = new OwnedSkillBarrierAdapter();
            await using var composed = new Composition(f, adapter: adapter);
            var parent = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".net-test-artifacts"));
            Directory.CreateDirectory(parent);
            using var native = new NativeUxFixtureSession(parent);
            await native.InitializeAsync();
            var store = new WindowsOwnedDeclarativeSkillStore(native);
            var document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "Example", "Description",
                parameterized ? "Review ${input:topic}" : "Inert guidance",
                guidanceFormat: parameterized ? OwnedSkillGuidanceFormat.RequiredLiteralInputs : OwnedSkillGuidanceFormat.Literal);
            await store.Publish(document, static () => true, commit => Task.FromResult(commit()), native.Token);
            await using var admission = new OwnedSkillControlAdmission(native.Interactions, native.Interactions, new(native.Tasks));
            var workflow = new OwnedSkillInvocationWorkflow(store, admission, composed.Access, f.Store, composed.Workspace,
                composed.Conversation, native.Audit, f.Time);
            await using var use = new OwnedSkillInvocationController(native.Main, workflow, static () => true, static () => 1,
                NullLogger<OwnedSkillInvocationController>.Instance);
            use.Bind();
            OwnedSkillInvocationWindow? observed = null;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<OwnedSkillInvocationWindow>((window, _) => observed = window);
            var authority = new OwnedSkillAuthoringAuthority(RequestOrigin.LocalUi, static () => true);
            async Task Run(string action) => await use.Handle(new(action), authority, static () => true);
            await use.Handle(new("use", "example", "1.0.0"), authority, static () => true);
            var window = observed ?? throw new InvalidOperationException("No exact use window.");
            await Run("enable");
            await Run("targets");
            window.Targets.SelectedIndex = 0;
            window.Request.Text = "original private request";
            if (parameterized) { window.LiteralInputs.Inputs["topic"].Text = "private literal"; }
            await Run("review-use");
            var running = Run("invoke");
            await adapter.Entered.Task.WaitAsync(f.Token);
            if (privacy) { native.Main.CloseForObservedPrivacyEvent("Synthetic private-input withdrawal", hidePresentation: true); }
            var closing = use.CloseAndWait();
            closing.IsCompleted.Should().BeFalse();
            window.Instructions.Text.Should().BeEmpty();
            window.Request.Text.Should().BeEmpty();
            window.Confirmation.Text.Should().BeEmpty();
            window.LiteralInputs.Inputs.Should().BeEmpty();
            adapter.Release.SetResult(new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("late private answer", null)));
            await running;
            await closing;
            if (privacy) { await native.Main.PrivacyClosureTask; }
            window.Status.Text.Should().BeEmpty();
            var retained = (await f.Store.ReadConversationPageAsync(f.Request.SessionId, null, 25, f.Token)).Messages.Single();
            retained.AssistantResponse.Should().BeNull();
        });
    }

    private sealed class OwnedSkillBarrierAdapter : IModelTurnAdapter
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<ModelProviderReply> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
            CancellationToken token)
        {
            Entered.SetResult();
            return await Release.Task;
        }
    }
}
