using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task MissingRetentionApplicationContractAndRevokedNativeAdmissionNeverCreateControlIntent()
    {
        using var f = new Fixture();
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var unavailable = new Kora.Application.Hosting.SessionWorkspaceService(
            new Kora.Application.UnitTests.Configuration.AudioControlTestStore(), new(f), f, f.Logger);
        await unavailable.Invoking(s => s.ReviewRetentionApplyAsync(f.Session.SessionId, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await f.Service.Invoking(s => s.ApplyReviewedRetentionAsync(null!, () => false, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task RetentionApplicationUsesExistingAttachmentRevocationAndJoinBeforeCommit()
    {
        using var f = new Fixture();
        var audit = new FileAudit();
        var action = FileAction(new FileCapture(), audit);
        await using var attachments = FileService(f, action, audit);
        f.Service.BindAttachmentLifecycle(attachments);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var before = f.Retention;
        var review = new SessionRetentionApplyReview(Guid.NewGuid(), before,
            before.State with { DeleteDue = DateTimeOffset.UnixEpoch }, Kora.Core.Configuration.SessionRetentionSettings.Default,
            1, new(Guid.NewGuid(), new(f.Session, null), "source", 0, 0, 0, 0), DateTimeOffset.UnixEpoch, "head", "source");
        var revoked = false;
        attachments.Revoked += _ => revoked = true;
        await f.Service.ApplyReviewedRetentionAsync(review, () => true, f.Token);
        revoked.Should().BeTrue();
        attachments.IsQuiescent.Should().BeTrue();
    }

    private partial class Fixture : ISessionRetentionApplyStore
    {
        public ValueTask<SessionRetentionApplyReview> ReviewRetentionApplyAsync(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> session,
            CancellationToken token) => throw new InvalidOperationException("No confirmed native review.");
        public ValueTask ValidateRetentionApplyAsync(SessionRetentionApplyReview review, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<SessionRetentionApplyReceipt> ApplyReviewedRetentionAsync(Kora.Core.Hosting.HostRequest request,
            SessionRetentionApplyReview review, Func<bool> admitted, CancellationToken token) =>
            ValueTask.FromResult(new SessionRetentionApplyReceipt(review, review.Before, review.Effect));
    }
}
