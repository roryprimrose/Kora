using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Hosting;
using Kora.Application.Skills;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, false)]
    [InlineData(RequestOrigin.ActivatedVoice, false)]
    [InlineData(RequestOrigin.LocalUi, true)]
    [InlineData(RequestOrigin.ActivatedVoice, true)]
    public async Task NamedDuplicateChoiceCompletesCanonicalSeparateReviewsAndRetainsGenuineAnswer(RequestOrigin origin, bool sharedChoice)
    {
        using var f = new Fixture(origin);
        await using var own = new OwnedSkillHarness(f, origin);
        await using var external = new SharedSkillHarness(f);
        await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, external.Register);
        own.Store.Snapshot = own.Store.Snapshot! with
        { Document = OwnedDeclarativeSkillDocument.Create("example", "1.0.0", "concise-review", "Description", "literal guidance") };
        var parsed = NamedSkillCommand.Parse("Nova, use skill concise-review", "Nova")!;
        var inventory = new NamedSkillCatalogue(own.Store, own.Saved, external.Discovery, f, new NamedUsePolicy(), f.Time);
        using var candidates = await InspectNamed(inventory, parsed.Name, own.Authority, own.Eligible);
        candidates.Choices.Should().HaveCount(2);
        candidates.Choices.Select(row => row.Number).Should().Equal(1, 2);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        var selection = candidates.Choose(sharedChoice ? 2 : 1);
        const string body = "  exact USER task e\u0301\r\nowned skill invoke; Lock; use skill another  ";
        var target = new Kora.Core.Storage.SessionWorkspaceEntry(f.Session, null);
        Kora.Core.Conversation.ConversationSubmissionReceipt receipt;
        if (sharedChoice)
        {
            var flow = external.Workflow();
            using var review = await flow.ReviewAsync(selection.Source!, selection.Shared!, selection.Current, origin, Token);
            review.IsEnabled.Should().BeFalse();
            await flow.EnableAsync(review, selection.Current, origin, Token);
            using var invocation = await flow.PrepareAsync(review, target, body, selection.Current, origin, Token);
            f.Adapter.Calls.Should().Be(0);
            receipt = await flow.InvokeAsync(invocation, selection.Current, Token);
        }
        else
        {
            using var review = await selection.ReviewOwnedAsync(own.Workflow, Token);
            review.IsEnabled.Should().BeFalse();
            await own.Workflow.EnableAsync(review, own.Authority, selection.Current, Token);
            using var invocation = await own.Workflow.PrepareAsync(review, target, body, own.Authority, selection.Current, Token);
            f.Adapter.Calls.Should().Be(0);
            receipt = await own.Workflow.InvokeAsync(invocation,
                new(RequestOrigin.LocalUi, own.Eligible), selection.Current, Token);
        }
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        receipt.Message.AssistantResponse!.Body.Should().Be("answer");
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Request.SessionId.Should().Be(f.Session.SessionId);
        receipt.Message.Request.Origin.Should().Be(origin);
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(body);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        own.Store.Writes.Should().Be(0);
        f.Messages.Should().NotContain(message => message.Contains(body, StringComparison.Ordinal));
        ((Action)(() => candidates.Choose(1))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("owned-source")]
    [InlineData("shared-source")]
    [InlineData("retired")]
    [InlineData("wrong-kind")]
    public async Task NamedChoicesNeverReadLatestByNameOrChangeKindAfterInspection(string fault)
    {
        using var f = new Fixture();
        await using var own = new OwnedSkillHarness(f);
        await using var external = new SharedSkillHarness(f);
        await external.Register();
        var inventory = new NamedSkillCatalogue(own.Store, own.Saved, external.Discovery, f, new NamedUsePolicy(), f.Time);
        using var candidates = await InspectNamed(inventory, fault is "shared-source" or "wrong-kind" ? "concise-review" : "Example",
            own.Authority, own.Eligible);
        var selection = candidates.Choose(1);
        if (fault is "wrong-kind")
        {
            await selection.Invoking(row => row.ReviewOwnedAsync(own.Workflow, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        else if (fault is "shared-source")
        {
            external.Reader.Text += "\nChanged source.";
            var flow = external.Workflow();
            await flow.Invoking(value => value.ReviewAsync(selection.Source!, selection.Shared!, selection.Current, RequestOrigin.LocalUi, Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            if (fault is "retired") { candidates.Dispose(); }
            else { own.Store.Snapshot = own.Store.Snapshot! with { InstructionsIdentity = "replacement" }; }
            await selection.Invoking(row => row.ReviewOwnedAsync(own.Workflow, Token)).Should().ThrowAsync<Exception>();
        }
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        own.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("owner")]
    [InlineData("control")]
    [InlineData("inspect")]
    [InlineData("policy")]
    [InlineData("registration")]
    [InlineData("corrupt-owned")]
    [InlineData("corrupt-shared")]
    [InlineData("wrong-source")]
    [InlineData("cancel")]
    public async Task NamedInspectionCannotTurnUnknownRevokedOrMalformedCatalogueIntoSuccessfulEmptyLookup(string fault)
    {
        using var f = new Fixture();
        await using var own = new OwnedSkillHarness(f);
        await using var external = new SharedSkillHarness(f);
        await external.Register();
        var policy = new NamedUsePolicy();
        var inventory = new NamedSkillCatalogue(own.Store, own.Saved, external.Discovery, f, policy, f.Time);
        if (fault is "owner") { own.Open = false; }
        if (fault is "control") { f.CanControl = false; }
        if (fault is "inspect") { f.Inspect = false; }
        if (fault is "policy") { own.Store.DuringList = () => { policy.Revision++; return Task.CompletedTask; }; }
        if (fault is "registration")
        {
            own.Store.DuringList = async () =>
            {
                var sources = await external.Discovery.LoadSourcesAsync(external.Eligible, Token);
                await external.Discovery.UnregisterAsync(external.Reader.Source, sources, external.Eligible, Token);
            };
        }
        if (fault is "corrupt-owned") { own.Store.Failure = new InvalidDataException("invalid saved inventory"); }
        if (fault is "corrupt-shared") { external.Reader.Failure = new InvalidDataException("invalid registered inventory"); }
        if (fault is "wrong-source") { external.Reader.WrongSource = true; }
        using var cancelled = new CancellationTokenSource();
        if (fault is "cancel") { cancelled.Cancel(); }
        Func<Task> inspect = fault is "origin"
            ? async () =>
            {
                using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request);
                using var unexpected = await inventory.InspectAsync("Example", own.Authority, own.Eligible, Token);
            }
            : async () =>
            {
                using var unexpected = await InspectNamed(inventory, "Example", own.Authority, own.Eligible,
                    fault is "cancel" ? cancelled.Token : Token);
            };
        await inspect.Should().ThrowAsync<Exception>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        own.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("instructions")]
    [InlineData("manifest")]
    [InlineData("none")]
    public async Task NamedOwnedInclusionUsesExactHostOwnedSourcesWithoutInheritedEnablement(string excluded)
    {
        using var f = new Fixture();
        await using var own = new OwnedSkillHarness(f);
        await using var external = new SharedSkillHarness(f);
        var policy = new NamedUsePolicy();
        if (excluded is not "none")
        {
            policy.Disabled.Add(ArtifactUseIdentity.File(Path.Combine(own.Store.Destination(own.Store.Snapshot!.Document),
                excluded is "manifest" ? "manifest.yaml" : "instructions.md")));
        }
        var inventory = new NamedSkillCatalogue(own.Store, own.Saved, external.Discovery, f, policy, f.Time);
        using var candidates = await InspectNamed(inventory, "Example", own.Authority, own.Eligible);
        candidates.Choices.Single().IsSupported.Should().Be(excluded is "none");
        policy.Revision++;
        candidates.IsCurrent.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NamedInspectionCancellationRetainsActualIgnoringReaderUntilItJoins()
    {
        using var f = new Fixture();
        await using var own = new OwnedSkillHarness(f);
        await using var external = new SharedSkillHarness(f);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        own.Store.DuringList = async () => { entered.SetResult(); await release.Task; };
        var inventory = new NamedSkillCatalogue(own.Store, own.Saved, external.Discovery, f, new NamedUsePolicy(), f.Time);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var running = InspectNamed(inventory, "Example", own.Authority, own.Eligible, cancellation.Token);
        await entered.Task.WaitAsync(Token);
        cancellation.Cancel();
        running.IsCompleted.Should().BeFalse();
        release.SetResult();
        await ((Func<Task>)(async () => { using var result = await running; })).Should().ThrowAsync<OperationCanceledException>();
        f.Adapter.Calls.Should().Be(0);
        own.Store.Writes.Should().Be(0);
    }

    private static async Task<NamedSkillCandidates> InspectNamed(NamedSkillCatalogue catalogue, string name,
        OwnedSkillAuthoringAuthority authority, Func<bool> eligible, CancellationToken? cancellation = null)
    {
        NamedSkillCandidates? result = null;
        await HostRequestRunner.RunAsync(RequestOrigin.LocalUi,
            async () => result = await catalogue.InspectAsync(name, authority, eligible, cancellation ?? Token));
        return result!;
    }

    private sealed class NamedUsePolicy : IArtifactUsePolicy
    {
        public event EventHandler? Changed { add { } remove { } }
        public long Revision { get; set; }
        internal HashSet<string> Disabled { get; } = [];
        public bool IsDisabled(string identity) => Disabled.Contains(identity);
        public bool IsRevisionAllowed(string identity, string digest) => !IsDisabled(identity);
    }
}
