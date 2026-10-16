using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Authorization;

namespace Kora.Core.UnitTests.Authorization;

public sealed partial class ExactGrantHistoryQueryTests
{
    [Fact]
    public void CorrelationAndApprovalChoicesComposeWithEveryCommittedPredicateAndBindDistinctDigests()
    {
        ExactGrantHistoryQuery.ApprovalOptions.Should().Equal("Any", "Present", "Absent", "Exact");
        var row = Audit(SecurityAuditOutcome.Denied, "policy-denied");
        var correlation = row.Audit.CorrelationId;
        var approval = Guid.NewGuid();
        var other = Guid.NewGuid();
        var digests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wantedCorrelation in new Guid?[] { null, correlation, other })
        {
            foreach (var choice in Enum.GetValues<ExactGrantHistoryApproval>())
            {
                foreach (var wantedApproval in choice == ExactGrantHistoryApproval.Exact ? new Guid?[] { approval, other } : [null])
                {
                    var query = ExactGrantHistoryQuery.FromInput("Denied", "policy-denied", "LocalUser", "Present",
                        row.CommittedAt.ToString("O"), row.CommittedAt.ToString("O"), row.DueAt.ToString("O"), row.DueAt.ToString("O"),
                        wantedCorrelation?.ToString("D") ?? "", choice.ToString(), wantedApproval?.ToString("D") ?? "");
                    query.Should().Be(new ExactGrantHistoryQuery(row.Audit.Outcome, row.Audit.ReasonCode, row.Audit.Initiator,
                        row.Retention, row.CommittedAt, row.CommittedAt, row.DueAt, row.DueAt, wantedCorrelation, choice, wantedApproval));
                    query.AuditCorrelationId.Should().Be(wantedCorrelation);
                    query.Approval.Should().Be(choice);
                    query.ApprovalId.Should().Be(wantedApproval);
                    digests.Add(query.Digest).Should().BeTrue();
                    foreach (var actualApproval in new Guid?[] { null, approval, other })
                    {
                        var committed = new SecurityAuditEvent(correlation, row.Audit.Category, row.Audit.ActionId,
                            row.Audit.Outcome, row.Audit.Initiator, row.Audit.TargetId, actualApproval, row.Audit.ReasonCode);
                        var entry = row with { Audit = committed };
                        var expectedApproval = choice switch
                        {
                            ExactGrantHistoryApproval.Any => true,
                            ExactGrantHistoryApproval.Present => actualApproval.HasValue,
                            ExactGrantHistoryApproval.Absent => !actualApproval.HasValue,
                            _ => actualApproval == wantedApproval,
                        };
                        query.Matches(entry).Should().Be((wantedCorrelation is null || wantedCorrelation == correlation) && expectedApproval);
                        query.Matches(entry with { Audit = committed.WithOutcome(SecurityAuditOutcome.Succeeded, "policy-denied") })
                            .Should().BeFalse();
                    }
                    query.Digest.Should().NotBe(ExactGrantHistoryQuery.Unfiltered.Digest);
                }
            }
        }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("12345678123412341234123456789abc")]
    [InlineData("{12345678-1234-1234-1234-123456789abc}")]
    [InlineData("12345678-1234-1234-1234-123456789ABC")]
    [InlineData(" 12345678-1234-1234-1234-123456789abc")]
    [InlineData("12345678-1234-1234-1234-123456789abc ")]
    [InlineData("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx")]
    public void BothNativeIdsRequireStrictNonemptyCanonicalLowercaseDGuid(string input)
    {
        var correlation = () => ExactGrantHistoryQuery.FromInput("Any", "", auditCorrelationId: input);
        var approval = () => ExactGrantHistoryQuery.FromInput("Any", "", approval: "Exact", approvalId: input);
        correlation.Should().Throw<InvalidDataException>();
        approval.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("any")]
    [InlineData("present")]
    [InlineData("Absent ")]
    [InlineData(" Exact")]
    [InlineData("0")]
    [InlineData("Unknown")]
    public void UnknownApprovalChoiceCannotMeanAnyOrAbsent(string? choice)
    {
        var parse = () => ExactGrantHistoryQuery.FromInput("Any", "", approval: choice!);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExactApprovalDoesNotMatchRequestedReviewOrTypedGrantProvenanceAndAbsentIsNotUnknown()
    {
        var row = Audit(SecurityAuditOutcome.Denied, "policy-denied");
        var id = Guid.NewGuid();
        var requested = new SecurityAuditEvent(row.Audit.CorrelationId, row.Audit.Category, row.Audit.ActionId,
            SecurityAuditOutcome.Requested, row.Audit.Initiator, row.Audit.TargetId, id);
        row = row with { Provenance = row.Provenance with { RequestedAudit = requested, GrantId = new(id) } };
        new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Exact, approvalId: id).Matches(row).Should().BeFalse();
        new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Present).Matches(row).Should().BeFalse();
        new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Absent).Matches(row).Should().BeTrue();
        var unknown = () => new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Absent).Matches(row with { Audit = null! });
        unknown.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ApprovalIdIsRequiredOnlyForExactAndMalformedMetadataFailsBeforeExclusion()
    {
        var id = Guid.NewGuid();
        foreach (var choice in new[] { ExactGrantHistoryApproval.Any, ExactGrantHistoryApproval.Present, ExactGrantHistoryApproval.Absent })
        {
            var typed = () => new ExactGrantHistoryQuery(approval: choice, approvalId: id);
            var native = () => ExactGrantHistoryQuery.FromInput("Any", "", approval: choice.ToString(), approvalId: id.ToString("D"));
            typed.Should().Throw<InvalidDataException>();
            native.Should().Throw<InvalidDataException>();
        }
        Action[] invalid =
        [
            () => _ = new ExactGrantHistoryQuery(auditCorrelationId: Guid.Empty),
            () => _ = new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Exact, approvalId: Guid.Empty),
            () => _ = new ExactGrantHistoryQuery(approval: (ExactGrantHistoryApproval)(-1)),
            () => _ = new ExactGrantHistoryQuery(approval: ExactGrantHistoryApproval.Exact),
        ];
        foreach (var create in invalid) { create.Should().Throw<InvalidDataException>(); }
        var nullCorrelation = () => ExactGrantHistoryQuery.FromInput("Any", "", auditCorrelationId: null!);
        var nullApproval = () => ExactGrantHistoryQuery.FromInput("Any", "", approvalId: null!);
        nullCorrelation.Should().Throw<ArgumentNullException>();
        nullApproval.Should().Throw<ArgumentNullException>();
        var row = Audit(SecurityAuditOutcome.Succeeded, null);
        row = row with { Audit = new(id, row.Audit.Category, row.Audit.ActionId, row.Audit.Outcome,
            row.Audit.Initiator, row.Audit.TargetId, Guid.Empty) };
        var exclude = () => new ExactGrantHistoryQuery(SecurityAuditOutcome.Denied,
            auditCorrelationId: Guid.NewGuid(), approval: ExactGrantHistoryApproval.Absent).Matches(row);
        exclude.Should().Throw<InvalidDataException>();
    }
}
