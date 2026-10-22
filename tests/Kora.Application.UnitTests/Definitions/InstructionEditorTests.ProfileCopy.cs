using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Fact]
    public async Task ProfileForkSeparatesReadConsentFromDisabledPublicationAndPreservesOpenOwnedDraft()
    {
        await using var f = new Fixture();
        using var reader = new ProfileReader();
        var review = new ProfileReview();
        var original = f.Files.Current!;
        var workflow = new ProfileInstructionForkWorkflow(reader, f.Admission, f.Actions, f.Audit);
        var result = await workflow.Fork(reader.Source.Metadata.CanonicalPath, "separate", review, f.Eligible, CancellationToken.None);
        reader.Source.Reads.Should().Be(1);
        reader.Source.Rechecks.Should().Be(1);
        reader.Source.Disposed.Should().BeTrue();
        review.ReadReviews.Should().Be(1);
        review.CopyReviews.Should().Be(1);
        review.Copy!.Source.Document.Text.Should().Be(reader.Source.Text);
        result.Published.Should().BeTrue();
        result.Current!.Document.Definition.Source.Should().Be("kora-profile");
        result.Current.Document.Metadata["attribution"].Should().Contain(review.Copy.Source.Identity);
        f.Files.Current.Should().BeSameAs(original);
        f.Catalogue.Publish(f.Revisions.Reconcile([Candidate(original), Candidate(result.Current)]));
        f.Catalogue.Artifacts.Should().BeEmpty();
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded,
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => !item.TargetId.Contains("Users", StringComparison.Ordinal)
            && !item.TargetId.Contains("Guidance", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledReadOrCopyConsentClearsLeaseWithoutWriting(bool cancelCopy)
    {
        await using var f = new Fixture();
        using var reader = new ProfileReader();
        var review = new ProfileReview { ReadAllowed = cancelCopy, CopyAllowed = !cancelCopy };
        var workflow = new ProfileInstructionForkWorkflow(reader, f.Admission, f.Actions, f.Audit);
        var result = await workflow.Fork(reader.Source.Metadata.CanonicalPath, "separate", review, f.Eligible, CancellationToken.None);
        result.Published.Should().BeFalse();
        f.Files.Writes.Should().Be(0);
        reader.Source.Reads.Should().Be(cancelCopy ? 1 : 0);
        reader.Source.Rechecks.Should().Be(0);
        reader.Source.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData("changed-bytes")]
    [InlineData("replaced")]
    [InlineData("withdrawn")]
    [InlineData("withdraw-before-read")]
    [InlineData("cancel-before-read")]
    [InlineData("replaced-during-read")]
    [InlineData("cancel")]
    [InlineData("session")]
    [InlineData("malformed")]
    [InlineData("unsupported")]
    [InlineData("read-failed")]
    [InlineData("audit-read")]
    [InlineData("audit-request")]
    [InlineData("storage")]
    [InlineData("collision")]
    [InlineData("readback")]
    [InlineData("audit-terminal")]
    [InlineData("catalogue")]
    public async Task ProfileForkFailuresNeverOverwriteSourceOrInheritUseAndAlwaysReleaseLease(string failure)
    {
        await using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        using var reader = new ProfileReader();
        var original = f.Files.Current!;
        var review = new ProfileReview();
        review.BeforeRead = () =>
        {
            if (failure is "withdraw-before-read") { f.Open = false; }
            if (failure is "cancel-before-read") { cancellation.Cancel(); }
        };
        review.BeforeCopy = () =>
        {
            if (failure is "changed-bytes") { reader.Source.Changed = true; }
            if (failure is "replaced") { reader.Source.Id = Guid.NewGuid(); }
            if (failure is "withdrawn") { f.Open = false; }
            if (failure is "cancel") { cancellation.Cancel(); }
            if (failure is "session") { f.Control.Authority = f.Control.Authority! with { IsActive = false }; }
        };
        if (failure is "malformed") { reader.Source.Text = "not native frontmatter"; }
        if (failure is "unsupported") { reader.Source.Text = reader.Source.Text.Replace("kora-format: 1", "kora-format: 2", StringComparison.Ordinal); }
        reader.Source.FailRead = failure is "read-failed";
        reader.Source.ReplaceDuringRead = failure is "replaced-during-read";
        f.Files.FailWrite = failure is "storage";
        f.Files.Unconfirmed = failure is "readback";
        if (failure is "collision")
        { f.Files.Copies["separate"] = new("separate", "a", "b", original.Document.WithOwnedCopyName("separate")); }
        f.Audit.BeforeWrite = item =>
        {
            if (failure is "audit-read" && item.ActionId.EndsWith("profile-read", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Requested
                || failure is "audit-request" && item.ActionId.EndsWith(".copy", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Requested
                || failure is "audit-terminal" && item.ActionId.EndsWith(".copy", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("Audit unavailable"); }
        };
        var workflow = new ProfileInstructionForkWorkflow(reader, f.Admission, f.Actions, f.Audit);
        var operation = () => workflow.Fork(reader.Source.Metadata.CanonicalPath, "separate", review, f.Eligible, cancellation.Token,
            _ => failure is not "catalogue");
        await operation.Should().ThrowAsync<Exception>();
        reader.Source.Disposed.Should().Be(failure is not "collision");
        if (failure is "collision") { reader.Selections.Should().Be(0); }
        f.Files.Current.Should().BeSameAs(original);
        f.Catalogue.Artifacts.Should().BeEmpty();
        if (failure is not ("readback" or "audit-terminal" or "catalogue"))
        { f.Files.Writes.Should().Be(0); }
        if (failure is "audit-read" or "withdraw-before-read" or "cancel-before-read") { reader.Source.Reads.Should().Be(0); }
        if (failure is "collision")
        { f.Files.Copies["separate"].Document.Definition.Content.Should().Be("Original"); }
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task ProfileForkDoesNotPromoteNonNativeOriginalInputIntoReadOrWriteAuthority(RequestOrigin origin)
    {
        await using var f = new Fixture();
        using var input = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        using var reader = new ProfileReader();
        var workflow = new ProfileInstructionForkWorkflow(reader, f.Admission, f.Actions, f.Audit);
        var operation = () => workflow.Fork(reader.Source.Metadata.CanonicalPath, "separate", new ProfileReview(), f.Eligible, CancellationToken.None);
        await operation.Should().ThrowAsync<InvalidOperationException>();
        reader.Selections.Should().Be(0);
        f.Files.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileForkAuditRemainsHostSessionCorrelatedAndTerminatesWithoutPrivateTraceData(bool failRead)
    {
        await using var f = new Fixture();
        using var reader = new ProfileReader();
        reader.Source.FailRead = failRead;
        var observations = new List<(SecurityAuditEvent Audit, Activity Trace, Guid Session)>();
        f.Audit.BeforeWrite = audit =>
        {
            var host = HostActivity.RequireCurrent();
            audit.CorrelationId.Should().Be(host.Request.RequestId.Value);
            observations.Add((audit, host.Activity!, host.Request.SessionId.Value));
        };
        var workflow = new ProfileInstructionForkWorkflow(reader, f.Admission, f.Actions, f.Audit);
        var operation = () => workflow.Fork(reader.Source.Metadata.CanonicalPath, "separate", new ProfileReview(), f.Eligible, CancellationToken.None);
        if (failRead) { await operation.Should().ThrowAsync<IOException>(); }
        else { (await operation()).Published.Should().BeTrue(); }
        observations.Should().HaveCount(failRead ? 2 : 4);
        observations.Select(item => item.Session).Distinct().Should().ContainSingle();
        observations[0].Trace.Should().BeSameAs(observations[1].Trace);
        observations[0].Trace.Status.Should().Be(failRead ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
        observations.Should().OnlyContain(item => item.Trace.IsStopped && item.Trace.IdFormat == ActivityIdFormat.W3C
            && item.Trace.Baggage.Count() == 0);
        var tags = string.Join(" ", observations.SelectMany(item => item.Trace.TagObjects).Select(item => item.Value));
        tags.Should().NotContain("Guidance").And.NotContain("Users").And.NotContain("separate");
        HostActivity.Current.Should().BeNull();
    }

    private sealed class ProfileReview : IProfileInstructionCopyReview
    {
        internal bool ReadAllowed = true;
        internal bool CopyAllowed = true;
        internal int ReadReviews;
        internal int CopyReviews;
        internal Action? BeforeCopy;
        internal Action? BeforeRead;
        internal ProfileInstructionCopy? Copy;
        public Task<bool> ConfirmRead(LocalFileMetadata source, CancellationToken token)
        { ReadReviews++; BeforeRead?.Invoke(); return Task.FromResult(ReadAllowed); }
        public Task<bool> ConfirmCopy(ProfileInstructionCopy copy, CancellationToken token)
        { CopyReviews++; Copy = copy; BeforeCopy?.Invoke(); return Task.FromResult(CopyAllowed); }
    }

    private sealed class ProfileReader : IProfileInstructionReader, IDisposable
    {
        internal readonly ProfileSelection Source = new();
        internal int Selections;
        public Task<IProfileInstructionSelection> OpenSelection(string selectedPath, CancellationToken token)
        { Selections++; return Task.FromResult<IProfileInstructionSelection>(Source); }
        public void Dispose() => Source.Dispose();
    }

    private sealed class ProfileSelection : IProfileInstructionSelection
    {
        internal string Text = "---\nkora-format: 1\nname: example\ndescription: Test\n---\nGuidance";
        internal int Reads;
        internal int Rechecks;
        internal bool Disposed;
        internal bool Changed;
        internal bool FailRead;
        internal bool ReplaceDuringRead;
        public Guid Id { get; set; } = Guid.NewGuid();
        public LocalFileMetadata Metadata => new(@"C:\Users\example\AppData\Roaming\Code\User\prompts\example.instructions.md",
            "exact-file", ArtifactFileDocument.Encode(Text, true).Length, DateTimeOffset.UnixEpoch);
        public Task<byte[]> Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Reads++;
            if (ReplaceDuringRead) { Id = Guid.NewGuid(); }
            return FailRead ? throw new IOException("Read failed") : Task.FromResult(ArtifactFileDocument.Encode(Text, true));
        }
        public Task<InstructionWriteResult> WithCurrent(ProfileInstructionSnapshot expected, Func<bool> consentCurrent,
            Func<Func<bool>, Task<InstructionWriteResult>> publish, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Rechecks++;
            if (Disposed || !consentCurrent() || expected.SourceId != Id || expected.Metadata != Metadata || Changed)
            { throw new InvalidOperationException("Source or consent changed"); }
            return publish(() => !Disposed && consentCurrent() && expected.SourceId == Id && expected.Metadata == Metadata && !Changed);
        }
        public void Dispose() => Disposed = true;
    }
}
