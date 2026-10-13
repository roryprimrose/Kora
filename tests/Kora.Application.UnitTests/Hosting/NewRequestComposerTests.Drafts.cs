using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Fact]
    public async Task SaveRequiresConfirmedFreshTargetAndItsOwnFrozenReviewAndRetainsExactBaselineWithoutSubmission()
    {
        using var f = new Fixture();
        var c = f.Composer;
        c.CanReviewSave.Should().BeFalse();
        c.CanCloseSaved.Should().BeFalse();
        c.SavedRevision.Should().Be(0);
        c.SavedBodyReview.Should().BeEmpty();
        c.ProposedSavedBody.Should().BeEmpty();
        c.BaselineBody.Should().BeEmpty();
        c.Body = "  exact e\u0301🙂\r\nend  ";
        await c.ReviewSave();
        (await c.Save()).Should().BeFalse();
        f.Creates.Should().Be(0);
        await c.Create();
        c.Review();
        await c.ReviewSave();
        c.CanSubmit.Should().BeFalse();
        c.CanSave.Should().BeTrue();
        c.ProposedSavedBody.Should().Be(c.Body);
        c.SavedBodyReview.Should().BeEmpty();
        (await c.Save()).Should().BeTrue();
        c.Body.Should().Be("  exact e\u0301🙂\r\nend  ");
        c.BaselineBody.Should().Be(c.Body);
        c.Dirty.Should().BeFalse();
        c.CanCloseSaved.Should().BeTrue();
        c.SavedRevision.Should().Be(1);
        c.CanSubmit.Should().BeFalse();
        c.CanSave.Should().BeFalse();
        (await c.Save()).Should().BeFalse();
        f.Saves.Should().Be(1);
        f.Retentions.Should().Be(0);
        c.Body = c.Body;
        c.Dirty.Should().BeFalse();
        c.CanCloseSaved.Should().BeFalse();
        c.Body = string.Empty;
        c.Dirty.Should().BeTrue();
        await c.ReviewSave();
        c.SavedBodyReview.Should().Be(c.BaselineBody);
        c.ProposedSavedBody.Should().BeEmpty();
        c.Review();
        c.CanSave.Should().BeFalse();
        await c.ReviewSave();
        (await c.Save()).Should().BeTrue();
        c.SavedRevision.Should().Be(2);
        c.Dirty.Should().BeFalse();
        c.Body = "separate submitted USER";
        c.Review();
        await c.Submit();
        f.Draft!.Draft.Body.Should().BeEmpty();
        c.Dirty.Should().BeFalse();
        c.CanReviewSave.Should().BeFalse();
        await c.ReviewSave();
        (await c.Save()).Should().BeFalse();
    }

    [Theory]
    [InlineData("same-bytes")]
    [InlineData("cancel")]
    [InlineData("submit-review")]
    [InlineData("control")]
    [InlineData("inspect")]
    [InlineData("close")]
    public async Task SaveReviewNeverSurvivesAnotherIntentOrControlEpoch(string revoke)
    {
        using var f = new Fixture();
        var c = f.Composer;
        await c.Create();
        c.Body = "memory";
        await c.ReviewSave();
        if (revoke is "same-bytes") { c.Body = c.Body; }
        if (revoke is "cancel") { c.Cancel(); }
        if (revoke is "submit-review") { c.Review(); }
        if (revoke is "control") { f.Access.ControlRevision++; }
        if (revoke is "inspect") { await c.Inspect(); }
        if (revoke is "close") { c.Dispose(); }
        c.CanSave.Should().BeFalse();
        (await c.Save()).Should().BeFalse();
        f.Saves.Should().Be(0);
        if (revoke is "control")
        {
            await c.ReviewSave();
            c.CanSave.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("metadata")]
    [InlineData("generation")]
    [InlineData("done")]
    [InlineData("draft-generation")]
    [InlineData("draft-session")]
    [InlineData("invalid-snapshot")]
    [InlineData("known-revision")]
    [InlineData("edit")]
    public async Task SaveReviewRefusesChangedObservedAuthorityOrKnownSnapshotWithoutFollowingNewerRevision(string fault)
    {
        using var f = new Fixture();
        var c = f.Composer;
        await c.Create();
        c.Body = "candidate";
        if (fault is "known-revision")
        {
            await c.ReviewSave();
            (await c.Save()).Should().BeTrue();
            f.Draft = f.Draft! with { Draft = f.Draft.Draft with { Revision = 2, Body = "another window" } };
        }
        if (fault is "metadata") { f.Entry = f.Entry! with { Metadata = f.Entry!.Metadata! with { Revision = new(2) } }; }
        if (fault is "generation") { f.Entry = f.Entry! with { Authority = f.Entry!.Authority with { Generation = new(2) } }; }
        if (fault is "done") { f.Entry = f.Entry! with { Authority = f.Entry!.Authority with { IsActive = false } }; }
        f.OnDraftRead = () =>
        {
            if (fault is "edit") { c.Body = c.Body; }
            if (fault is "draft-generation") { f.Draft = new(f.Entry!.Authority with { Generation = new(2) }, new(f.Entry.Authority.SessionId, 0, null)); }
            if (fault is "draft-session") { f.Draft = new(f.Entry!.Authority, new(new(Guid.NewGuid()), 0, null)); }
            if (fault is "invalid-snapshot") { f.Draft = new(f.Entry!.Authority, new(f.Entry.Authority.SessionId, -1, null)); }
        };
        await c.ReviewSave();
        c.CanSave.Should().BeFalse();
        c.CanReviewSave.Should().BeFalse();
        c.Body.Should().Be("candidate");
        await c.Inspect();
        c.CanReviewSave.Should().BeFalse();
        (await c.Save()).Should().BeFalse();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("same-bytes")]
    [InlineData("cancel")]
    [InlineData("control")]
    [InlineData("revision")]
    [InlineData("private")]
    [InlineData("close")]
    [InlineData("metadata")]
    [InlineData("stale")]
    [InlineData("audit")]
    [InlineData("readback")]
    [InlineData("late")]
    [InlineData("session")]
    [InlineData("target")]
    [InlineData("draft-session")]
    [InlineData("revision-receipt")]
    [InlineData("body")]
    [InlineData("post-metadata")]
    public async Task SaveCommitAndExactReceiptUncertaintyRetainMemoryAndNeverReleaseHoldsOrRetry(string fault)
    {
        using var f = new Fixture();
        var c = f.Composer;
        await c.Create();
        c.Body = "exact memory";
        await c.ReviewSave();
        if (fault is "metadata") { f.Entry = f.Entry! with { Metadata = f.Entry!.Metadata! with { Revision = new(2) } }; }
        f.BeforeSave = () =>
        {
            if (fault is "edit") { c.Body = "newer memory"; }
            if (fault is "same-bytes") { c.Body = c.Body; }
            if (fault is "cancel") { c.Cancel(); }
            if (fault is "control") { f.Access.CanControl = false; }
            if (fault is "revision") { f.Access.ControlRevision++; }
            if (fault is "private") { f.Access.CanInspect = false; }
            if (fault is "close") { c.Dispose(); }
            if (fault is "stale") { f.Draft = f.Draft! with { Draft = f.Draft.Draft with { Revision = 1, Body = "named writer" } }; }
        };
        f.AfterSave = () =>
        {
            if (fault is "late") { c.Cancel(); }
            if (fault is "post-metadata") { f.Entry = f.Entry! with { Metadata = f.Entry!.Metadata! with { Revision = new(2) } }; }
        };
        f.SaveFault = fault is "revision-receipt" ? "revision" : fault;
        (await c.Save()).Should().BeFalse();
        c.CanCloseSaved.Should().BeFalse();
        c.CanReviewSave.Should().BeFalse();
        if (fault is "private" or "close") { c.Body.Should().BeEmpty(); }
        else { c.Body.Should().Be(fault is "edit" ? "newer memory" : "exact memory"); }
        f.Access.CanInspect = true;
        f.Access.CanControl = true;
        f.BeforeSave = null;
        f.AfterSave = null;
        await c.Inspect();
        c.Review();
        await c.ReviewSave();
        var saves = f.Saves;
        (await c.Save()).Should().BeFalse();
        await c.Submit();
        f.Saves.Should().Be(saves);
        f.Retentions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveRejectsCredentialMaterialUsingMandatoryContentFreeAuditAndOnlyKnownRejectionAllowsCorrection(bool auditFailure)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        const string candidate = "api_key=synthetic-only-value";
        f.Composer.Body = candidate;
        await f.Composer.ReviewSave();
        f.RejectionFailure = auditFailure;
        (await f.Composer.Save()).Should().BeFalse();
        f.Saves.Should().Be(0);
        f.Composer.Body.Should().Be(candidate);
        f.Composer.Status.Should().NotContain(candidate);
        f.Composer.Body = "corrected";
        f.RejectionFailure = false;
        await f.Composer.ReviewSave();
        (await f.Composer.Save()).Should().Be(!auditFailure);
    }

    [Fact]
    public async Task HeldCreationOrSubmissionCannotBePromotedByDraftSaving()
    {
        using var f = new Fixture();
        f.TerminalFailure = true;
        await f.Composer.Create();
        await f.Composer.Inspect();
        await f.Composer.ReviewSave();
        f.Composer.CanSave.Should().BeFalse();
        using var s = new Fixture();
        await s.Composer.Create();
        s.Composer.Body = "memory";
        s.Composer.Review();
        s.CompletionFailure = true;
        await s.Composer.Submit();
        await s.Composer.ReviewSave();
        s.Composer.CanSave.Should().BeFalse();
        s.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("expired")]
    public async Task SavingCannotRelabelInheritedOrExpiredSourceAuthority(string source)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        f.Composer.Body = "original";
        await f.Composer.ReviewSave();
        using var root = HostActivity.BeginRoot(HostRequest.Create(source is "provider" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        if (source is "expired") { root.Activity!.Stop(); }
        (await f.Composer.Save()).Should().BeFalse();
        f.Saves.Should().Be(0);
    }

    [Fact]
    public async Task AwaitedSaveIsOneShotAndSameByteEditsFencePhysicalCommit()
    {
        using var f = new Fixture();
        await f.Composer.Create();
        f.Composer.Body = "candidate";
        await f.Composer.ReviewSave();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.SaveWait = async () => { entered.SetResult(); await release.Task; };
        var save = f.Composer.Save();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        (await f.Composer.Save()).Should().BeFalse();
        await f.Composer.ReviewSave();
        f.Composer.Body = f.Composer.Body;
        release.SetResult();
        (await save).Should().BeFalse();
        f.Saves.Should().Be(1);
        f.Draft!.Draft.Body.Should().BeNull();
    }
}
