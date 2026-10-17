using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Authorization;

public sealed partial class ExactGrantHistoryQueryTests
{
    [Fact]
    public void RequestIdentityPredicatesComposeIndependentlyWithAllCommittedPredicatesAndDistinctVersionedDigests()
    {
        var row = Audit(SecurityAuditOutcome.Denied, "policy-denied");
        var request = row.Request;
        var approval = Guid.NewGuid();
        row = row with { Audit = new(row.Audit.CorrelationId, row.Audit.Category, row.Audit.ActionId,
            row.Audit.Outcome, row.Audit.Initiator, row.Audit.TargetId, approval, row.Audit.ReasonCode) };
        var digests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in new Guid?[] { null, request.SessionId.Value, request.RequestId.Value })
        {
            foreach (var intent in new Guid?[] { null, request.RequestId.Value, request.TaskId.Value })
            {
                foreach (var task in new Guid?[] { null, request.TaskId.Value, request.SessionId.Value })
                {
                    var query = ExactGrantHistoryQuery.FromInput("Denied", "policy-denied", "LocalUser", "Present",
                        row.CommittedAt.ToString("O"), row.CommittedAt.ToString("O"), row.DueAt.ToString("O"), row.DueAt.ToString("O"),
                        row.Audit.CorrelationId.ToString("D"), "Exact", approval.ToString("D"),
                        session?.ToString("D") ?? "", intent?.ToString("D") ?? "", task?.ToString("D") ?? "");
                    query.Should().Be(new ExactGrantHistoryQuery(row.Audit.Outcome, row.Audit.ReasonCode, row.Audit.Initiator,
                        row.Retention, row.CommittedAt, row.CommittedAt, row.DueAt, row.DueAt,
                        row.Audit.CorrelationId, ExactGrantHistoryApproval.Exact, approval, session, intent, task));
                    query.SessionId.Should().Be(session);
                    query.RequestId.Should().Be(intent);
                    query.TaskId.Should().Be(task);
                    digests.Add(query.Digest).Should().BeTrue();
                    query.Matches(row).Should().Be((session is null || session == request.SessionId.Value)
                        && (intent is null || intent == request.RequestId.Value) && (task is null || task == request.TaskId.Value));
                    query.Matches(row with { Audit = row.Audit.WithOutcome(SecurityAuditOutcome.Succeeded, "policy-denied") })
                        .Should().BeFalse();
                }
            }
        }
        digests.Should().HaveCount(27);
        var canonical = "exact-grant-history-query-v5\nany\n\nany\nany\n\n\n\n\n\n0\n\n\n\n";
        ExactGrantHistoryQuery.Unfiltered.Digest.Should().Be(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        ExactGrantHistoryQuery.FromInput("Any", "").Should().Be(ExactGrantHistoryQuery.Unfiltered);
        ExactGrantHistoryQuery.Unfiltered.SessionId.Should().BeNull();
        ExactGrantHistoryQuery.Unfiltered.RequestId.Should().BeNull();
        ExactGrantHistoryQuery.Unfiltered.TaskId.Should().BeNull();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("Any")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("12345678123412341234123456789abc")]
    [InlineData("{12345678-1234-1234-1234-123456789abc}")]
    [InlineData("(12345678-1234-1234-1234-123456789abc)")]
    [InlineData("12345678-1234-1234-1234-123456789ABC")]
    [InlineData(" 12345678-1234-1234-1234-123456789abc")]
    [InlineData("12345678-1234-1234-1234-123456789abc ")]
    [InlineData("12345678-1234-1234-1234-123456789abc\n")]
    [InlineData("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx")]
    [InlineData("12345678-1234-1234-1234-123456789ab")]
    [InlineData("12345678-1234-1234-1234-123456789abcd")]
    public void AllRequestIdentityInputsReuseStrictCanonicalGuidAdmissionWithoutRepair(string input)
    {
        Action[] parses =
        [
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", sessionId: input),
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", requestId: input),
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", taskId: input),
        ];
        foreach (var parse in parses) { parse.Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void RequestIdentityBoundsRejectOversizeNullAndTypedEmptyWithoutDefaultFallback()
    {
        Action[] oversized =
        [
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", sessionId: new string('a', 4096)),
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", requestId: new string('a', 4096)),
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", taskId: new string('a', 4096)),
            () => _ = new ExactGrantHistoryQuery(sessionId: Guid.Empty),
            () => _ = new ExactGrantHistoryQuery(requestId: Guid.Empty),
            () => _ = new ExactGrantHistoryQuery(taskId: Guid.Empty),
        ];
        foreach (var parse in oversized) { parse.Should().Throw<InvalidDataException>(); }
        Action[] missing =
        [
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", sessionId: null!),
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", requestId: null!),
            () => _ = ExactGrantHistoryQuery.FromInput("Any", "", taskId: null!),
        ];
        foreach (var parse in missing) { parse.Should().Throw<ArgumentNullException>(); }
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void CanonicalNonemptyGuidValueBoundsAreAcceptedIndependently(string input)
    {
        var value = Guid.ParseExact(input, "D");
        var query = ExactGrantHistoryQuery.FromInput("Any", "", sessionId: input, requestId: input, taskId: input);
        query.Should().Be(new ExactGrantHistoryQuery(sessionId: value, requestId: value, taskId: value));
    }

    [Fact]
    public void OnlyCommittedRequestFieldsMatchAndMissingRequestFailsBeforeExclusion()
    {
        var row = Audit(SecurityAuditOutcome.Succeeded, null);
        var host = row.Request;
        var query = new ExactGrantHistoryQuery(sessionId: host.SessionId.Value,
            requestId: host.RequestId.Value, taskId: host.TaskId.Value);
        query.Matches(row).Should().BeTrue();
        query.Matches(row with { Request = new(host.RequestId, new(Guid.NewGuid()), host.TaskId, host.Origin) }).Should().BeFalse();
        query.Matches(row with { Request = new(new(Guid.NewGuid()), host.SessionId, host.TaskId, host.Origin) }).Should().BeFalse();
        query.Matches(row with { Request = new(host.RequestId, host.SessionId, new(Guid.NewGuid()), host.Origin) }).Should().BeFalse();
        var other = HostRequest.Create(RequestOrigin.LocalUi);
        var unrelated = new ExactGrantHistoryQuery(sessionId: other.SessionId.Value,
            requestId: other.RequestId.Value, taskId: other.TaskId.Value);
        unrelated.Matches(row with
        {
            Audit = new(other.RequestId.Value, row.Audit.Category, row.Audit.ActionId,
                row.Audit.Outcome, row.Audit.Initiator, other.TaskId.Value.ToString("D"), other.SessionId.Value),
            Provenance = row.Provenance with { GrantId = new(other.SessionId.Value) },
        }).Should().BeFalse();
        var exclude = () => new ExactGrantHistoryQuery(SecurityAuditOutcome.Denied,
            sessionId: other.SessionId.Value).Matches(row with { Request = null! });
        exclude.Should().Throw<InvalidDataException>();
    }
}
