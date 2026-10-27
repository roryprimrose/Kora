using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Threading;

using AwesomeAssertions;

using Kora.Application;
using Kora.Application.Configuration;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Hosting.SessionRouting;
using Kora.Application.Tools;
using Kora.Application.Voice;
using Kora.Core.Conversation;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Tools;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class SessionRoutingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalTypedEntryPresentsTheGenuineAnswerAfterNativeDestinationReview(bool expanded)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            await f.Initialize(expanded);
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            f.Ui.Native.Main.CommandText = "Explain the Alpha design";
            var operation = f.Ui.Native.Main.RunTypedCommand.ExecuteAsync();
            await Task.WhenAny(operation, opening.Task);
            operation.IsCompleted.Should().BeFalse("the original typed route must await its exact native review");
            var review = await opening.Task;
            await Choose(review);
            Find<TextBlock>(review, "RoutingAcknowledgment").Text.Should().StartWith("Destination: Alpha.");
            Click(review, "RoutingSubmit");
            await operation;
            f.Ui.Native.Main.ResponseTitle.Should().Be("Request destination: Alpha.");
            f.Ui.Native.Main.ResponseBody.Should().Contain("Genuine second-turn answer");
            f.Adapter.Originals.Should().Equal("Explain the Alpha design", "Explain the Alpha design");
            f.Adapter.Calls.Should().Be(2);
            var page = await f.Ui.Native.Interactions.ReadConversationPageAsync(f.Ui.First.Authority.SessionId, null, 50, f.Ui.Native.Token);
            page.Messages.Should().ContainSingle().Which.AssistantResponse!.Body.Should().Be("Genuine second-turn answer");
        });
    }

    [Theory]
    [InlineData(false, RequestOrigin.LocalUi, false)]
    [InlineData(true, RequestOrigin.LocalUi, true)]
    [InlineData(false, RequestOrigin.ActivatedVoice, true)]
    [InlineData(true, RequestOrigin.ActivatedVoice, false)]
    public async Task AdmittedLocalRoutingThenSecondTurnRetainsGenuineAnswerInAcknowledgedSession(
        bool expanded, RequestOrigin origin, bool fresh)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            await f.Initialize(expanded);
            var selected = f.Ui.Model.SelectedSessionRecord;
            f.Ui.Model.ComposerBody = "independent input";
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            const string body = "  Original e\u0301 request\r\nunchanged  ";
            var operation = f.Routing.RouteAsync(body, origin, static () => true);
            var review = await AwaitOpening(operation, opening.Task);
            f.Adapter.Calls.Should().Be(1);
            f.Adapter.Requests[0].Origin.Should().Be(origin);
            f.Adapter.Requests[0].SessionId.Should().NotBe(f.Ui.First.Authority.SessionId);
            (await f.Ui.Native.Tasks.ReadTaskAsync(f.Adapter.Requests[0].TaskId, f.Ui.Native.Token))!.State
                .Should().Be(HostTaskState.Succeeded, "inference must have a durable terminal before native review");
            f.Turns.IsQuiescent.Should().BeTrue("the first physical turn must join before review");
            await Choose(review);
            Find<TextBlock>(review, "RoutingAcknowledgment").Text.Should().StartWith("Destination: Alpha.");
            Click(review, "RoutingCorrect");
            var destinations = Find<ComboBox>(review, "RoutingDestinations");
            var inventory = await f.Ui.Native.Interactions.ReadMetadataPageAsync(null, 50, f.Ui.Native.Token);
            destinations.Items.Cast<object>().Select(item => item.ToString()).Should().Equal(
                inventory.Records.Where(item => item.Authority.IsActive
                    && item.Authority.SessionId != f.Adapter.Requests[0].SessionId)
                    .Select((item, index) => (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ": " + (item.Metadata?.Name.Value ?? "Unnamed conversation"))
                    .Append("New conversation (fresh session)"),
                "all and only permitted user destinations are offered, never the routing control identity");
            destinations.SelectedIndex = fresh ? destinations.ItemCount - 1
                : Array.FindIndex(destinations.Items.Cast<object>().ToArray(),
                    item => item.ToString()!.EndsWith(": Beta", StringComparison.Ordinal));
            await Choose(review);
            Find<TextBlock>(review, "RoutingAcknowledgment").Text.Should()
                .StartWith(fresh ? "Destination: New conversation" : "Destination: Beta.");
            Click(review, "RoutingSubmit");
            var receipt = (await operation)!;
            var message = await f.Ui.Native.Interactions.ReadConversationMessageAsync(
                receipt.Target.Authority.SessionId, receipt.Submission.Message.Request.RequestId, f.Ui.Native.Token);
            message.Body.Should().Be(body);
            message.Request.Origin.Should().Be(origin);
            message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            message.AssistantResponse!.Body.Should().Be("Genuine second-turn answer");
            message.Request.Should().Be(f.Adapter.Requests[1]);
            message.Request.TaskId.Should().NotBe(f.Adapter.Requests[0].TaskId);
            if (!fresh) { message.Request.SessionId.Should().Be(f.Ui.Second.Authority.SessionId); }
            else { message.Request.SessionId.Should().NotBe(f.Ui.First.Authority.SessionId).And.NotBe(f.Ui.Second.Authority.SessionId); }
            var page = await f.Ui.Native.Interactions.ReadConversationPageAsync(message.Request.SessionId, null, 50, f.Ui.Native.Token);
            page.Messages.Should().ContainSingle().Which.Should().Be(message);
            f.Adapter.Calls.Should().Be(2);
            f.Adapter.Originals.Should().Equal(body, body);
            f.Audit.Events.Where(item => string.Equals(item.ActionId, "model.turn", StringComparison.Ordinal))
                .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
                    SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
                    SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
                    SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.Ui.Model.ComposerBody.Should().Be("independent input");
            f.Ui.Model.SelectedSessionRecord.Should().Be(selected);
            f.Ui.Window!.IsExpanded.Should().Be(expanded);
            f.Turns.IsQuiescent.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("duplicate-field")]
    [InlineData("wrong-evidence")]
    [InlineData("wrong-revision")]
    [InlineData("duplicate-candidate")]
    [InlineData("oversize")]
    [InlineData("denied")]
    [InlineData("unknown")]
    public async Task ActualAdmittedInferenceRejectsInvalidOutputWithoutReviewFallbackOrConversation(string fault)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            await f.Initialize(expanded: true);
            f.Adapter.Fault = fault;
            var windows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((_, _) => windows++);
            await f.Routing.Invoking(subject => subject.RouteAsync("private original", RequestOrigin.LocalUi, static () => true))
                .Should().ThrowAsync<Exception>();
            windows.Should().Be(0);
            f.Adapter.Calls.Should().Be(1);
            var task = await f.Ui.Native.Tasks.ReadTaskAsync(f.Adapter.Requests[0].TaskId, f.Ui.Native.Token);
            task!.State.Should().Be(fault is "denied" ? HostTaskState.Denied
                : fault is "unknown" or "oversize" ? HostTaskState.Unknown : HostTaskState.Failed);
            var page = await f.Ui.Native.Interactions.ReadConversationPageAsync(f.Ui.First.Authority.SessionId, null, 50, f.Ui.Native.Token);
            page.Messages.Should().BeEmpty();
            f.Turns.IsQuiescent.Should().BeTrue();
        });
    }

    [Fact]
    public async Task ActualUnqualifiedLocalHostOffersOnlyReviewedNewWithoutProviderContactOrAutomaticCreation()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            await f.Initialize(expanded: false, qualified: false);
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            var operation = f.Routing.RouteAsync("original", RequestOrigin.ActivatedVoice, static () => true);
            var window = await AwaitOpening(operation, opening.Task);
            Find<ComboBox>(window, "RoutingDestinations").Items.Cast<object>()
                .Select(item => item.ToString()).Should().Equal("New conversation (fresh session)");
            f.Adapter.Calls.Should().Be(0);
            Click(window, "RoutingCancel");
            (await operation).Should().BeNull();
            var entries = await f.Ui.Native.Interactions.ReadMetadataPageAsync(null, 50, f.Ui.Native.Token);
            entries.Records.Should().HaveCount(6, "only the routing control domain was added, never a user fallback");
        });
    }

    [Fact]
    public async Task WithdrawalDuringGenuineAnswerJoinsTheSecondProviderAndNeverRetainsALateAnswer()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            await f.Initialize(expanded: true);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.HoldAnswer = async () => { entered.SetResult(); await release.Task; };
            var opening = new TaskCompletionSource<SessionRoutingWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((window, _) => opening.TrySetResult(window));
            var operation = f.Routing.RouteAsync("original", RequestOrigin.ActivatedVoice, static () => true);
            var window = await AwaitOpening(operation, opening.Task);
            await Choose(window);
            Click(window, "RoutingSubmit");
            await Task.WhenAny(entered.Task, operation);
            if (operation.IsCompleted) { await operation; throw new InvalidOperationException("The answering turn did not start."); }
            await entered.Task;
            var shutdown = f.Routing.PrepareShutdown();
            try
            {
                shutdown.IsCompleted.Should().BeFalse();
                f.Turns.IsQuiescent.Should().BeFalse();
            }
            finally { release.TrySetResult(); }
            (await shutdown).Should().BeTrue();
            await this.Invoking(async _ => await operation).Should().ThrowAsync<Exception>();
            var page = await f.Ui.Native.Interactions.ReadConversationPageAsync(f.Ui.First.Authority.SessionId, null, 50, f.Ui.Native.Token);
            page.Messages.Should().ContainSingle().Which.AssistantResponse.Should().BeNull();
            f.Adapter.Calls.Should().Be(2);
            f.Turns.IsQuiescent.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData("dispose")]
    [InlineData("privacy")]
    [InlineData("input")]
    [InlineData("control")]
    [InlineData("metadata")]
    [InlineData("call")]
    public async Task NoncooperativeRoutingInferenceIsPhysicallyJoinedAndCannotOfferLateReviewOrSubmit(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            await using var f = new LocalComposition();
            await f.Initialize(expanded: true);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Adapter.Hold = async () => { entered.SetResult(); await release.Task; };
            var live = true;
            var windows = 0;
            using var opened = Window.WindowOpenedEvent.AddClassHandler<SessionRoutingWindow>((_, _) => windows++);
            var operation = f.Routing.RouteAsync("private original", RequestOrigin.ActivatedVoice, () => live);
            await Task.WhenAny(entered.Task, operation);
            if (operation.IsCompleted) { await operation; throw new InvalidOperationException("Inference never started."); }
            await entered.Task;
            Task<bool>? shutdown = null;
            try
            {
                if (change is "dispose") { f.Routing.Dispose(); }
                if (change is "privacy") { f.Ui.Native.Main.CloseForObservedPrivacyEvent("Synthetic routing closure", hidePresentation: true); }
                if (change is "input" or "call") { live = false; }
                if (change is "control") { f.Ui.Native.Access.SetOpen(false); }
                if (change is "metadata")
                {
                    await f.Ui.Native.Sessions.RenameAsync(f.Ui.First.Authority.SessionId, f.Ui.First.Authority.Generation,
                        f.Ui.First.Metadata!.Revision.Value, new("Changed"), RequestOrigin.LocalUi, f.Ui.Native.Token);
                }
                shutdown = f.Routing.PrepareShutdown();
                shutdown.IsCompleted.Should().BeFalse("withdrawal cannot abandon a cancellation-ignoring provider");
                f.Turns.IsQuiescent.Should().BeFalse();
                windows.Should().Be(0);
            }
            finally { release.TrySetResult(); }
            (await shutdown!).Should().BeTrue();
            await this.Invoking(async _ => await operation).Should().ThrowAsync<Exception>();
            Dispatcher.UIThread.RunJobs();
            windows.Should().Be(0);
            f.Adapter.Calls.Should().Be(1);
            f.Turns.IsQuiescent.Should().BeTrue();
            var page = await f.Ui.Native.Interactions.ReadConversationPageAsync(
                f.Ui.First.Authority.SessionId, null, 50, f.Ui.Native.Token);
            page.Messages.Should().BeEmpty();
        });
    }

    private sealed class LocalComposition : IAsyncDisposable
    {
        internal Fixture Ui { get; } = new();
        internal LocalRoutingAdapter Adapter { get; } = new();
        internal SessionRoutingController Routing { get; private set; } = null!;
        internal ModelTurnHost Turns { get; private set; } = null!;
        internal RoutingAudit Audit { get; } = new();
        private AudioControlAdmission preferences = null!;
        private SessionRoutingControlAdmission admission = null!;

        internal async Task Initialize(bool expanded, bool qualified = true)
        {
            await Ui.Initialize(expanded);
            Ui.Routing.Dispose();
            Adapter.TargetName = Ui.First.Metadata!.Name.Value;
            var native = Ui.Native;
            preferences = new(native.Interactions, native.Interactions, new(native.Tasks));
            var modes = new ProviderModeConfigurationService(new LocalModelProviderModePreferences(native), preferences, native.Audit);
            modes.Observe();
            var dependencies = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
            var runtime = new Kora.Tools.Runtime.RecordedRuntimeObservation(dependencies);
            var registry = new ReadOnlyCapabilityRegistry(native.Access, new(), new(), new(new AssemblyApplicationInfo()),
                new(dependencies), new(runtime), new(runtime), NullLogger<ReadOnlyCapabilityRegistry>.Instance);
            Audit.Inner = native.Audit;
            Turns = new(native.Interactions, native.Access, native.Access, registry, Audit, NullLogger<ModelTurnHost>.Instance,
                TimeProvider.System, [new(ModelProviderSelection.OllamaCandidate,
                    qualified ? ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost : ModelQualificationGate.None,
                    DateTimeOffset.UtcNow.AddMinutes(5), Adapter)], modes);
            admission = new(native.Interactions, native.Interactions, new(native.Tasks));
            var conversation = new SessionConversationService(native.Interactions, native.Interactions, native.Sessions,
                native.Access, Turns, TimeProvider.System, NullLogger<SessionConversationService>.Instance,
                execution: Turns, cancellationAudit: native.Audit, interactions: native.Interactions);
            Routing = new(native.Main, native.Sessions, conversation, native.Access, TimeProvider.System,
                new LocalSessionRelatednessMatcher(Turns, Turns, TimeProvider.System), admission);
            Routing.Bind();
        }

        internal sealed class RoutingAudit : ISecurityAuditLog
        {
            internal ISecurityAuditLog? Inner { get; set; }
            internal List<SecurityAuditEvent> Events { get; } = [];
            public void Write(SecurityAuditEvent auditEvent)
            {
                Events.Add(auditEvent);
                Inner?.Write(auditEvent);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Routing is not null) { await Routing.PrepareShutdown(); Routing.Dispose(); }
            if (admission is not null) { await admission.DisposeAsync(); }
            if (Turns is not null) { await Turns.DisposeAsync(); }
            if (preferences is not null) { await preferences.DisposeAsync(); }
            Ui.Dispose();
        }
    }

    private sealed class LocalRoutingAdapter : IModelTurnAdapter
    {
        internal string TargetName { get; set; } = string.Empty;
        internal int Calls { get; private set; }
        internal string? Fault { get; set; }
        internal List<HostRequest> Requests { get; } = [];
        internal List<string> Originals { get; } = [];
        internal Func<Task>? Hold { get; set; }
        internal Func<Task>? HoldAnswer { get; set; }

        public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
            ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool, CancellationToken token)
        {
            Calls++;
            Requests.Add(provenance.Request);
            using var document = JsonDocument.Parse(context);
            var root = document.RootElement;
            Originals.Add(root.GetProperty("UserRequest").GetString()!);
            if (Calls == 1)
            {
                root.GetProperty("Tools").GetArrayLength().Should().Be(0);
                root.GetProperty("Evidence").GetArrayLength().Should().Be(1);
                using var metadata = JsonDocument.Parse(root.GetProperty("Evidence")[0].GetProperty("Content").GetString()!);
                var descriptor = metadata.RootElement.GetProperty("descriptors").EnumerateArray()
                    .Single(item => string.Equals(item.GetProperty("name").GetString(), TargetName, StringComparison.Ordinal));
                if (Hold is { } hold) { await hold(); }
                if (Fault is "denied" or "unknown")
                { return new(Fault is "denied" ? ModelTurnOutcome.Denied : ModelTurnOutcome.Unknown, provenance.Selection); }
                var candidate = new { index = descriptor.GetProperty("index").GetInt32(),
                    evidence = Fault is "wrong-evidence" ? Guid.NewGuid() : descriptor.GetProperty("evidence").GetGuid() };
                var answer = JsonSerializer.Serialize(new
                {
                    revision = metadata.RootElement.GetProperty("revision").GetInt64() + (Fault is "wrong-revision" ? 1 : 0),
                    match = Fault is "duplicate-candidate" ? "ambiguous" : "clear",
                    candidates = Fault is "duplicate-candidate" ? new[] { candidate, candidate } : [candidate],
                });
                answer = Fault switch
                {
                    "malformed" => "{",
                    "duplicate-field" => answer.Insert(1, "\"revision\":0,"),
                    "oversize" => new string('x', ModelContextEnvelope.MaximumOutputUtf8Bytes + 1),
                    _ => answer,
                };
                return new(ModelTurnOutcome.Succeeded, provenance.Selection, new(answer, null));
            }
            root.GetProperty("Evidence").GetArrayLength().Should().Be(0, "routing metadata is not conversation context");
            if (HoldAnswer is { } held) { await held(); }
            return new(ModelTurnOutcome.Succeeded, provenance.Selection, new("Genuine second-turn answer", null));
        }
    }
}
