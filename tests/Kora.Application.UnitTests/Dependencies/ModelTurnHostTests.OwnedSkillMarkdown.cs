using System.Collections.Immutable;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private static OwnedDeclarativeSkillSnapshot MarkdownPackage(string text = "Exact reference", string name = "Example") =>
        new(OwnedDeclarativeSkillDocument.CreatePackage("example", "1.0.0", name, "Description",
            @"Use [notes](references\notes.md) as inert guidance.",
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [@"references\notes.md"] = System.Text.Encoding.UTF8.GetBytes(text),
                ["rubric.md"] = "Review prose only."u8.ToArray(),
            }), "directory", "manifest", "instructions")
        { ReferenceIdentities = ImmutableDictionary<string, string>.Empty.Add(@"references\notes.md", "notes").Add("rubric.md", "rubric") };

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task OwnedMarkdownSeparatesReadConsentCompleteReviewEnableAndExactToolFreeAnswer(RequestOrigin origin)
    {
        using var f = new Fixture(origin);
        await using var skill = new OwnedSkillHarness(f, origin);
        skill.Store.Snapshot = MarkdownPackage();
        using var review = await skill.Review();
        review.RequiresReferenceConsent.Should().BeTrue();
        review.Text.Should().Contain(@"references\notes.md").And.NotContain("Exact reference");
        skill.Store.ReferenceReads.Should().Be(0);
        await skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token);
        review.RequiresReferenceConsent.Should().BeFalse();
        review.Text.Should().Contain("Exact reference").And.Contain("Review prose only.");
        review.IsEnabled.Should().BeFalse();
        skill.Store.ReferenceReads.Should().Be(1);
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Prepare(review, "unchanged original prose");
        f.Adapter.Calls.Should().Be(0);
        using (var preview = JsonDocument.Parse(invocation.Preview))
        {
            preview.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(4);
            preview.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        }
        var result = await skill.Workflow.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token);
        result.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        result.Message.Body.Should().Be("unchanged original prose");
        result.Message.AssistantResponse!.Body.Should().Be("answer");
        f.Adapter.Calls.Should().Be(1);
        f.ExecutionCompletions.Should().Be(1);
        skill.Store.Writes.Should().Be(0);
        review.Text.Should().BeEmpty();
        review.RequiresReferenceConsent.Should().BeFalse();
        skill.Store.Snapshot.Document.IsAvailableForInvocation.Should().BeFalse();
    }

    [Theory]
    [InlineData("no-consent")]
    [InlineData("missing-policy")]
    [InlineData("revoked-before-read")]
    [InlineData("revoked-during-read")]
    [InlineData("changed-primary")]
    [InlineData("changed-during-read")]
    [InlineData("missing")]
    [InlineData("privacy")]
    [InlineData("repeat-read")]
    [InlineData("literal")]
    public async Task OwnedMarkdownUnreviewedOrRevokedSourceNeverReachesModel(string fault)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f, hasUsePolicy: fault is not "missing-policy");
        if (fault is not "literal") { skill.Store.Snapshot = MarkdownPackage(); }
        if (fault is "missing-policy")
        {
            await skill.Invoking(item => item.Review()).Should().ThrowAsync<InvalidOperationException>();
            skill.Store.ReferenceReads.Should().Be(0);
            return;
        }
        using var review = await skill.Review();
        var identity = ArtifactUseIdentity.File(Path.Combine(skill.Store.Destination(skill.Store.Snapshot!.Document), @"references\notes.md"));
        if (fault is "no-consent")
        { await skill.Workflow.Invoking(item => item.EnableAsync(review, skill.Authority, skill.Eligible, Token)).Should().ThrowAsync<InvalidOperationException>(); }
        else
        {
            if (fault is "revoked-before-read") { skill.UsePolicy.Disabled.Add(identity); }
            if (fault is "revoked-during-read") { skill.Store.DuringReferenceRead = () => { skill.UsePolicy.Disabled.Add(identity); return Task.CompletedTask; }; }
            if (fault is "changed-primary") { skill.Store.Snapshot = MarkdownPackage(name: "Changed"); }
            if (fault is "changed-during-read")
            { skill.Store.DuringReferenceRead = () => { skill.Store.Snapshot = MarkdownPackage(name: "Changed"); return Task.CompletedTask; }; }
            if (fault is "missing") { skill.Store.Snapshot = null; }
            if (fault is "privacy") { f.CanControl = false; }
            if (fault is "repeat-read") { await skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token); }
            await skill.Workflow.Invoking(item => item.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token))
                .Should().ThrowAsync<Exception>();
        }
        if (fault is "no-consent" or "privacy" or "revoked-before-read" or "literal") { skill.Store.ReferenceReads.Should().Be(0); }
        review.IsCurrent.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("replacement")]
    [InlineData("policy")]
    [InlineData("control")]
    [InlineData("privacy")]
    public async Task OwnedMarkdownIndependentFreshnessAndRevocationRemainMandatoryAfterCapture(string fault)
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        skill.Store.Snapshot = MarkdownPackage();
        using var review = await skill.Review();
        await skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token);
        await skill.Workflow.EnableAsync(review, skill.Authority, skill.Eligible, Token);
        using var invocation = await skill.Prepare(review);
        if (fault is "bytes") { skill.Store.Snapshot = MarkdownPackage("Changed reference"); }
        if (fault is "replacement")
        { skill.Store.Snapshot = skill.Store.Snapshot with { ReferenceIdentities = skill.Store.Snapshot.ReferenceIdentities.SetItem(@"references\notes.md", "replacement") }; }
        if (fault is "policy") { skill.UsePolicy.Revision++; }
        if (fault is "control") { f.ControlRevision++; }
        if (fault is "privacy") { f.CanControl = false; }
        await skill.Workflow.Invoking(item => item.InvokeAsync(invocation, skill.Authority, skill.Eligible, Token)).Should().ThrowAsync<Exception>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
        review.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task OwnedMarkdownCancellationRetainsIgnoringReferenceReadUntilItJoinsAndSuppressesLateContent()
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        skill.Store.Snapshot = MarkdownPackage();
        using var review = await skill.Review();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        skill.Store.DuringReferenceRead = async () => { started.SetResult(); await release.Task; };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var capture = skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, cancellation.Token);
        await started.Task;
        cancellation.Cancel();
        capture.IsCompleted.Should().BeFalse();
        release.SetResult();
        await ((Func<Task>)(async () => await capture)).Should().ThrowAsync<OperationCanceledException>();
        review.Text.Should().BeEmpty();
        f.Adapter.Calls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }

    [Fact]
    public async Task OwnedMarkdownConcurrentConsentCannotReplaceAnAlreadyCompletedBundleOrRenewItsAuthority()
    {
        using var f = new Fixture();
        await using var skill = new OwnedSkillHarness(f);
        skill.Store.Snapshot = MarkdownPackage();
        using var review = await skill.Review();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        skill.Store.DuringReferenceRead = async () =>
        {
            if (++count == 1) { firstStarted.SetResult(); await releaseFirst.Task; }
            else { secondStarted.SetResult(); await releaseSecond.Task; }
        };
        var first = skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token);
        await firstStarted.Task;
        var second = skill.Workflow.ReadReferencesAsync(review, skill.Authority, skill.Eligible, Token);
        releaseFirst.SetResult();
        await first;
        await secondStarted.Task;
        releaseSecond.SetResult();
        await ((Func<Task>)(async () => await second)).Should().ThrowAsync<InvalidOperationException>();
        review.IsCurrent.Should().BeFalse();
        f.Adapter.Calls.Should().Be(0);
        skill.Store.Writes.Should().Be(0);
    }
}
