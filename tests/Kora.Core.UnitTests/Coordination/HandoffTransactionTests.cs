using AwesomeAssertions;

using Kora.Core.Coordination;

namespace Kora.Core.UnitTests.Coordination;

public sealed class HandoffTransactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstanceBuildIdentity OriginalBuild =
        new("0.2.0", "original", new string('A', 64), false, @"Q:\Original\Kora.exe", @"Q:\Original");
    private static readonly InstanceProcessIdentity Original =
        new("S-1-5-21-123", 1, 100, 1234, OriginalBuild);
    private static readonly InstanceProcessIdentity Candidate =
        new("S-1-5-21-123", 1, 200, 5678,
            OriginalBuild with { IsDebug = true, ContentDigest = new string('B', 64) });

    [Fact]
    public void Semantic_version_or_location_does_not_replace_content_identity()
    {
        OriginalBuild.IsSameBuild(OriginalBuild with { ExecutablePath = @"Q:\Other\Kora.exe" }).Should().BeTrue();
        OriginalBuild.IsSameBuild(OriginalBuild with { ContentDigest = new string('B', 64) }).Should().BeFalse();
        OriginalBuild.IsSameBuild(OriginalBuild with { IsDebug = true }).Should().BeFalse();
        var unknown = OriginalBuild with { ContentDigest = string.Empty };
        unknown.IsSameBuild(unknown).Should().BeFalse();
    }

    [Fact]
    public void Only_approved_and_actually_quiescent_transaction_issues_single_use_ticket()
    {
        var transaction = Create();
        transaction.IssueTicket(Now).Should().BeNull();
        transaction.MarkQuiescent(Now, hostDisposed: true, candidateAlive: true).Should().BeFalse();
        transaction.Approve(Now, eligible: true, candidateAlive: true).Should().BeTrue();
        transaction.IssueTicket(Now).Should().BeNull();
        transaction.MarkQuiescent(Now, hostDisposed: false, candidateAlive: true).Should().BeFalse();
        transaction.MarkQuiescent(Now, hostDisposed: true, candidateAlive: true).Should().BeTrue();
        var ticket = transaction.IssueTicket(Now)!;
        Commit(transaction, ticket, Candidate).Should().BeTrue();
        Commit(transaction, ticket, Candidate).Should().BeFalse();
        transaction.Stage.Should().Be(HandoffStage.Transferred);
        transaction.IssueTicket(Now).Should().BeNull();
    }

    [Fact]
    public void Unknown_cross_user_or_cross_session_identities_cannot_create_a_transaction()
    {
        foreach (var candidate in new[]
        {
            Candidate with { UserSid = "S-1-5-21-456" },
            Candidate with { SessionId = 2 },
            Candidate with { CreationTime = 0 },
            Candidate with { ProcessId = Original.ProcessId },
            Candidate with { ProcessId = 0 },
            Candidate with { Build = Candidate.Build with { ContentDigest = string.Empty } },
            Candidate with { Build = OriginalBuild },
        })
        {
            var action = () => new HandoffTransaction(Guid.NewGuid(), Original, candidate, Now.AddMinutes(1));
            action.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("candidate");
        }
    }

    [Fact]
    public void Null_original_identity_is_explicitly_rejected()
    {
        var action = () => new HandoffTransaction(Guid.NewGuid(), null!, Candidate, Now.AddMinutes(1));
        action.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("original");
    }

    [Fact]
    public void Null_candidate_identity_is_explicitly_rejected()
    {
        var action = () => new HandoffTransaction(Guid.NewGuid(), Original, null!, Now.AddMinutes(1));
        action.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("candidate");
    }

    [Fact]
    public void Empty_owner_epoch_is_explicitly_rejected()
    {
        var action = () => new HandoffTransaction(Guid.Empty, Original, Candidate, Now.AddMinutes(1));
        action.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("ownerEpoch");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-digest")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void Original_with_unproven_content_digest_is_rejected(string digest)
    {
        var original = Original with { Build = OriginalBuild with { ContentDigest = digest } };
        var action = () => new HandoffTransaction(Guid.NewGuid(), original, Candidate, Now.AddMinutes(1));
        action.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("original");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Original_without_a_user_sid_is_rejected(string? sid)
    {
        var original = Original with { UserSid = sid! };
        var action = () => new HandoffTransaction(Guid.NewGuid(), original, Candidate, Now.AddMinutes(1));
        action.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("original");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Each_nonpositive_original_process_identity_field_is_rejected(int invalid)
    {
        foreach (var original in new[]
        {
            Original with { SessionId = invalid },
            Original with { ProcessId = invalid },
            Original with { CreationTime = invalid },
        })
        {
            var action = () => new HandoffTransaction(Guid.NewGuid(), original, Candidate, Now.AddMinutes(1));
            action.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("original");
        }
    }

    [Theory]
    [InlineData(false, "Release")]
    [InlineData(true, "Debug")]
    public void Display_identity_contains_exact_build_mode_location_and_digest(bool isDebug, string mode)
    {
        var build = OriginalBuild with { IsDebug = isDebug };
        build.DisplayName.Should().Be(
            $"Kora {build.Version}, {mode}, build {build.BuildId}\n" +
            $"{build.ExecutablePath}\nDeployment: {build.DeploymentIdentity}\nSHA-256: {build.ContentDigest}");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Lock_or_candidate_death_blocks_approval(bool eligible, bool candidateAlive)
    {
        var transaction = Create();
        transaction.Approve(Now, eligible, candidateAlive).Should().BeFalse();
        transaction.IssueTicket(Now).Should().BeNull();
    }

    [Fact]
    public void Expiry_blocks_every_transition()
    {
        var transaction = Create();
        var expired = Now.AddMinutes(1);
        transaction.Approve(expired, eligible: true, candidateAlive: true).Should().BeFalse();
        transaction.Approve(Now, eligible: true, candidateAlive: true).Should().BeTrue();
        transaction.MarkQuiescent(expired, hostDisposed: true, candidateAlive: true).Should().BeFalse();
        transaction.MarkQuiescent(Now, hostDisposed: true, candidateAlive: true).Should().BeTrue();
        transaction.IssueTicket(expired).Should().BeNull();
        transaction.Commit(transaction.OwnerEpoch, transaction.Id, transaction.IssueTicket(Now)!,
            Candidate, expired, eligible: true).Should().BeFalse();
    }

    [Fact]
    public void Pid_reuse_user_session_creation_and_binary_changes_cannot_redeem_ticket()
    {
        var transaction = Quiescent();
        var ticket = transaction.IssueTicket(Now)!;
        Commit(transaction, ticket, Candidate with { CreationTime = Candidate.CreationTime + 1 }).Should().BeFalse();
        Commit(transaction, ticket, Candidate with { UserSid = "S-1-5-21-456" }).Should().BeFalse();
        Commit(transaction, ticket, Candidate with { SessionId = 2 }).Should().BeFalse();
        Commit(transaction, ticket, Candidate with { ProcessId = 201 }).Should().BeFalse();
        Commit(transaction, ticket, Candidate with { Build = OriginalBuild }).Should().BeFalse();
        Commit(transaction, ticket, Candidate).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("forged")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Malformed_or_forged_ticket_is_denied(string ticket)
    {
        Commit(Quiescent(), ticket, Candidate).Should().BeFalse();
    }

    [Fact]
    public void Stale_epoch_transaction_and_lock_cannot_commit()
    {
        var transaction = Quiescent();
        var ticket = transaction.IssueTicket(Now)!;
        transaction.Commit(Guid.NewGuid(), transaction.Id, ticket, Candidate, Now, eligible: true).Should().BeFalse();
        transaction.Commit(transaction.OwnerEpoch, Guid.NewGuid(), ticket, Candidate, Now, eligible: true).Should().BeFalse();
        transaction.Commit(transaction.OwnerEpoch, transaction.Id, ticket, Candidate, Now, eligible: false).Should().BeFalse();
        Commit(transaction, ticket, Candidate).Should().BeTrue();
    }

    [Fact]
    public void Decline_invalidates_all_future_use()
    {
        var transaction = Quiescent();
        var ticket = transaction.IssueTicket(Now)!;
        transaction.Cancel();
        transaction.Stage.Should().Be(HandoffStage.Cancelled);
        transaction.IssueTicket(Now).Should().BeNull();
        Commit(transaction, ticket, Candidate).Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_commit_has_exactly_one_winner()
    {
        var transaction = Quiescent();
        var ticket = transaction.IssueTicket(Now)!;
        var results = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => Commit(transaction, ticket, Candidate))));
        results.Count(result => result).Should().Be(1);
    }

    private static HandoffTransaction Create() => new(Guid.NewGuid(), Original, Candidate, Now.AddMinutes(1));

    private static HandoffTransaction Quiescent()
    {
        var transaction = Create();
        transaction.Approve(Now, eligible: true, candidateAlive: true);
        transaction.MarkQuiescent(Now, hostDisposed: true, candidateAlive: true);
        return transaction;
    }

    private static bool Commit(HandoffTransaction transaction, string ticket, InstanceProcessIdentity candidate) =>
        transaction.Commit(transaction.OwnerEpoch, transaction.Id, ticket, candidate, Now, eligible: true);
}
