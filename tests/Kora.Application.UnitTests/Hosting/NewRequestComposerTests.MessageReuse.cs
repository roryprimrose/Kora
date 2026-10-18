using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    private static ConversationMessage Original(Fixture f, bool active = true)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        f.Entry = new(new(request.SessionId, new(1), active), null);
        f.ReuseOriginal = new(request, new(1), 7, 2, DateTimeOffset.UnixEpoch,
            "  exact original e\u0301🙂\r\n\t<script>inert</script>  ", ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
        return f.ReuseOriginal;
    }

    private static Task<OriginalMessageReuseReview> ReviewOriginal(Fixture f, ConversationMessage original,
        Func<bool>? admitted = null, CancellationToken? token = null) =>
        OriginalMessageReuseReview.ReviewAsync(f.Workspace, f.Access, f.Entry!, original, f.Composer,
            admitted ?? (() => true), token ?? TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OriginalReuseIsExactMemoryOnlyWithHistoricalAttributionAndFreshEventualSubmission(bool active)
    {
        using var f = new Fixture();
        var original = Original(f, active);
        var source = f.Entry;
        using (var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request))
        using (var review = await ReviewOriginal(f, original))
        {
            review.Available.Should().BeTrue();
            review.RequiresReplacement.Should().BeFalse();
            review.PreviousBody.Should().BeEmpty();
            review.Body.Should().Be(original.Body);
            review.Attribution.Should().Contain(original.Request.SessionId.Value.ToString("D"))
                .And.Contain(original.Request.RequestId.Value.ToString("D"))
                .And.Contain(original.Request.TaskId.Value.ToString("D"))
                .And.Contain("USER sequence 7").And.Contain("record revision 2")
                .And.Contain(active ? "Active" : "Done").And.NotContain("HOST Unavailable");
            f.Creates.Should().Be(0);
            f.Writes.Should().Be(0);
            f.Retentions.Should().Be(0);
            f.Saves.Should().Be(0);
            f.OriginalReads.Should().Be(2);
            System.ComponentModel.PropertyChangedEventHandler checkHandoff = (_, _) =>
            { if (f.Composer.Body == original.Body) { review.Available.Should().BeTrue(); } };
            f.Composer.PropertyChanged += checkHandoff;
            try { (await review.ConfirmAsync(false, TestContext.Current.CancellationToken)).Should().BeTrue(); }
            finally { f.Composer.PropertyChanged -= checkHandoff; }
            f.OriginalReads.Should().Be(4);
            f.Composer.Body.Should().Be(original.Body);
            f.Composer.Dirty.Should().BeTrue();
            f.Composer.Creation.Should().BeNull();
            f.Composer.Target.Should().BeNull();
            f.Composer.Submission.Should().BeNull();
            f.Composer.CanSave.Should().BeFalse();
            f.Composer.CanSubmit.Should().BeFalse();
            f.Composer.Status.Should().Contain("memory only").And.Contain("No task replay");
            f.Entry.Should().Be(source);
            f.ReuseOriginal.Should().BeSameAs(original);
            f.Records.Should().BeEmpty();
            review.Available.Should().BeFalse();
            review.Body.Should().BeEmpty();
            review.PreviousBody.Should().BeEmpty();
            review.Attribution.Should().BeEmpty();
            review.RequiresReplacement.Should().BeFalse();
            (await review.ConfirmAsync(true, TestContext.Current.CancellationToken)).Should().BeFalse();
        }
        // Completed copying is independent memory, not a source-dependent replay grant.
        f.ReuseOriginal = null;
        await f.Composer.Create();
        f.Composer.Creation!.Request.SessionId.Should().NotBe(original.Request.SessionId);
        f.Composer.Review();
        await f.Composer.Submit();
        f.Retained!.Body.Should().Be(original.Body);
        f.Retained.Request.RequestId.Should().NotBe(original.Request.RequestId);
        f.Retained.Request.TaskId.Should().NotBe(original.Request.TaskId);
        f.Retained.Request.SessionId.Should().NotBe(original.Request.SessionId);
        f.Retentions.Should().Be(1);
        original.Body.Should().Be("  exact original e\u0301🙂\r\n\t<script>inert</script>  ");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirtyNewRequestRequiresReviewOfCompleteOldMemoryAndExplicitReplacement(bool replace)
    {
        using var f = new Fixture();
        var original = Original(f);
        f.Composer.Body = "  KEEP existing e\u0301🙂\r\n";
        using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
        using var review = await ReviewOriginal(f, original);
        review.RequiresReplacement.Should().BeTrue();
        review.PreviousBody.Should().Be(f.Composer.Body);
        (await review.ConfirmAsync(replace, TestContext.Current.CancellationToken)).Should().Be(replace);
        f.Composer.Body.Should().Be(replace ? original.Body : "  KEEP existing e\u0301🙂\r\n");
        f.Creates.Should().Be(0);
        f.Writes.Should().Be(0);
        review.Available.Should().BeFalse();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("edit")]
    [InlineData("same-bytes")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("created")]
    [InlineData("dispose")]
    public async Task ChangedAdmissionAndTargetMemoryPermanentlyRetireWholeReuseReview(string change)
    {
        using var f = new Fixture();
        var original = Original(f);
        f.Composer.Body = "existing independent memory";
        var admitted = true;
        using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
        using var review = await ReviewOriginal(f, original, () => admitted);
        var retired = 0;
        review.Retired += (_, _) => retired++;
        switch (change)
        {
            case "source": admitted = false; break;
            case "private": f.Access.CanInspect = false; break;
            case "control": f.Access.CanControl = false; break;
            case "revision": f.Access.ControlRevision++; break;
            case "edit": f.Composer.Body = "newer edits"; break;
            case "same-bytes": f.Composer.Body = f.Composer.Body; break;
            case "cancel": f.Composer.Cancel(); break;
            case "close": f.Composer.Dispose(); break;
            case "created": await f.Composer.Create(); break;
            case "dispose": review.Dispose(); break;
        }
        review.Available.Should().BeFalse();
        admitted = true;
        f.Access.CanInspect = f.Access.CanControl = true;
        review.Available.Should().BeFalse();
        review.Body.Should().BeEmpty();
        review.PreviousBody.Should().BeEmpty();
        review.Attribution.Should().BeEmpty();
        (await review.ConfirmAsync(true, TestContext.Current.CancellationToken)).Should().BeFalse();
        retired.Should().Be(1);
        f.Composer.Body.Should().NotBe(original.Body);
        f.Retentions.Should().Be(0);
        f.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("body")]
    [InlineData("revision")]
    [InlineData("generation")]
    [InlineData("lifecycle")]
    [InlineData("cancel")]
    [InlineData("private")]
    [InlineData("control")]
    public async Task ConfirmationRereadsExactOriginalAndNeverFollowsNewerOrUnavailableSource(string change)
    {
        using var f = new Fixture();
        var original = Original(f);
        f.Composer.Body = "existing";
        using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
        using var review = await ReviewOriginal(f, original);
        using var cancellation = new CancellationTokenSource();
        f.BeforeOriginalRead = () =>
        {
            switch (change)
            {
                case "source": f.ReuseOriginal = null; break;
                case "body": f.ReuseOriginal = original with { Body = "changed source" }; break;
                case "revision": f.ReuseOriginal = original with { Revision = 1, Outcome = null, Reason = null }; break;
                case "generation": f.Entry = f.Entry! with { Authority = f.Entry!.Authority with { Generation = new(2) } }; break;
                case "lifecycle": f.Entry = f.Entry! with { Authority = f.Entry!.Authority with { IsActive = false } }; break;
                case "cancel": cancellation.Cancel(); break;
                case "private": f.Access.CanInspect = false; break;
                case "control": f.Access.ControlRevision++; break;
            }
        };
        Func<Task> confirm = () => review.ConfirmAsync(true, cancellation.Token);
        await confirm.Should().ThrowAsync<Exception>();
        review.Available.Should().BeFalse();
        f.Composer.Body.Should().Be("existing");
        f.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("edit")]
    [InlineData("close")]
    [InlineData("cancel")]
    public async Task SourceRevocationOrReentrancyDuringSeedBindingsErasesUncompletedHandoff(string change)
    {
        using var f = new Fixture();
        var original = Original(f);
        f.Composer.Body = "old buffer";
        var admitted = true;
        using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
        using var review = await ReviewOriginal(f, original, () => admitted);
        var changed = false;
        f.Composer.PropertyChanged += (_, _) =>
        {
            if (changed || f.Composer.Body != original.Body) { return; }
            changed = true;
            switch (change)
            {
                case "source": admitted = false; break;
                case "private": f.Access.CanInspect = false; break;
                case "control": f.Access.ControlRevision++; break;
                case "edit": f.Composer.Body = "reentrant edit"; break;
                case "close": f.Composer.Dispose(); break;
                case "cancel": f.Composer.Cancel(); break;
            }
        };
        (await review.ConfirmAsync(true, TestContext.Current.CancellationToken)).Should().BeFalse();
        f.Composer.Body.Should().Be(change is "private" or "close" ? string.Empty
            : change is "edit" ? "reentrant edit" : "old buffer");
        f.Creates.Should().Be(0);
        f.Retentions.Should().Be(0);
        f.Writes.Should().Be(0);
        review.Available.Should().BeFalse();
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("invalid")]
    [InlineData("oversize")]
    [InlineData("unicode")]
    [InlineData("controls")]
    [InlineData("credentials")]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("private")]
    [InlineData("control")]
    [InlineData("admission")]
    [InlineData("created")]
    [InlineData("invalid-buffer")]
    public async Task ReuseRefusesUnadmittedMalformedScreenedOrUnavailableFullBodiesWithoutRepair(string fault)
    {
        using var f = new Fixture();
        var original = Original(f);
        switch (fault)
        {
            case "foreign": original = original with { Request = HostRequest.Create(RequestOrigin.LocalUi) }; break;
            case "invalid": original = original with { Revision = 0 }; break;
            case "oversize": original = original with { Body = new string('x', 4097) }; break;
            case "unicode": original = original with { Body = "\ud800" }; break;
            case "controls": original = original with { Body = "hidden\0bytes" }; break;
            case "credentials": original = original with { Body = "-----BEGIN PRIVATE KEY-----\nMII-private-key-material-1234\n-----END PRIVATE KEY-----" }; break;
            case "missing": f.ReuseOriginal = null; break;
            case "stale": f.Entry = f.Entry! with { Authority = f.Entry!.Authority with { SessionId = new(Guid.NewGuid()) } }; break;
            case "private": f.Access.CanInspect = false; break;
            case "control": f.Access.CanControl = false; break;
            case "created": await f.Composer.Create(); break;
            case "invalid-buffer": f.Composer.Body = "\ud800"; break;
        }
        using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
        Func<Task> read = () => ReviewOriginal(f, original, () => fault is not "admission");
        await read.Should().ThrowAsync<Exception>();
        f.Composer.Body.Should().Be(fault is "invalid-buffer" ? "\ud800" : string.Empty);
        f.Saves.Should().Be(0);
        f.Retentions.Should().Be(0);
    }

    [Theory]
    [InlineData("no-host")]
    [InlineData("system")]
    [InlineData("foreign-session")]
    [InlineData("invocation")]
    public async Task ReuseNeverFabricatesNativeSessionAuthorityFromIncomingCorrelation(string fault)
    {
        using var f = new Fixture();
        var original = Original(f);
        var request = new HostRequest(new(Guid.NewGuid()), fault is "foreign-session" ? new(Guid.NewGuid()) : original.Request.SessionId,
            new(Guid.NewGuid()), fault is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            fault is "invocation" ? new(Guid.NewGuid()) : null);
        using var root = fault is "no-host" ? null : HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
        Func<Task> read = () => ReviewOriginal(f, original);
        await read.Should().ThrowAsync<InvalidOperationException>();
        f.OriginalReads.Should().Be(0);
        f.Composer.Body.Should().BeEmpty();
        f.Writes.Should().Be(0);
    }

    [Fact]
    public async Task RepeatedConfirmationWhileExactReadIsPendingRetiresProposalAndLateReadCannotSeed()
    {
        using var f = new Fixture();
        var original = Original(f);
        using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
        using var review = await ReviewOriginal(f, original);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.OriginalReadWait = async () => { entered.TrySetResult(); await release.Task; };
        var pending = review.ConfirmAsync(true, TestContext.Current.CancellationToken);
        await entered.Task;
        (await review.ConfirmAsync(true, TestContext.Current.CancellationToken)).Should().BeFalse();
        release.SetResult();
        Func<Task> finish = () => pending;
        await finish.Should().ThrowAsync<InvalidOperationException>();
        f.Composer.Body.Should().BeEmpty();
        f.Writes.Should().Be(0);
    }

    [Fact]
    public async Task LifecycleNotificationsRetireOnlyExactSourceWithoutAReplayOrTargetCreation()
    {
        using var f = new Fixture();
        var original = Original(f);
        using var root = HostActivity.BeginRoot(original.Request, HostActivityLayer.Desktop, HostOperation.Request);
        using var review = await ReviewOriginal(f, original);
        await f.Workspace.ChangeLifecycleAsync(new(Guid.NewGuid()), new(1), false, RequestOrigin.LocalUi, TestContext.Current.CancellationToken);
        review.Available.Should().BeTrue();
        await f.Workspace.ChangeLifecycleAsync(original.Request.SessionId, new(2), false, RequestOrigin.LocalUi, TestContext.Current.CancellationToken);
        review.Available.Should().BeFalse();
        review.Body.Should().BeEmpty();
        f.Composer.Body.Should().BeEmpty();
        f.Creates.Should().Be(0);
        f.Retentions.Should().Be(0);
    }
}
