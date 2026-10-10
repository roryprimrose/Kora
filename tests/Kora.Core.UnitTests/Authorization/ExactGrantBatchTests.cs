using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Authorization;

public sealed class ExactGrantBatchTests
{
    [Fact]
    public void CompleteSerializedBatchAcceptsExactly64KiBAndRejectsOneAdditionalByteWithoutTruncation()
    {
        var batch = new ExactGrantBatch(Guid.NewGuid(), [Inspection()]);
        batch.Validate();
        var padding = ExactGrantPage.MaximumBytes - JsonSerializer.SerializeToUtf8Bytes(batch).Length;
        batch = batch with { Records = [batch.Records[0] with { StoreIdentity = "store" + new string('a', padding) }] };
        JsonSerializer.SerializeToUtf8Bytes(batch).Length.Should().Be(ExactGrantPage.MaximumBytes);
        batch.Validate();
        var oversized = batch with { Records = [batch.Records[0] with { StoreIdentity = batch.Records[0].StoreIdentity + "a" }] };
        var validate = oversized.Validate;
        validate.Should().Throw<InvalidDataException>();
        oversized.Records.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void InvalidSelectionNeverBecomesAReviewedExactBatch(int invalid)
    {
        var item = Inspection();
        var batch = new ExactGrantBatch(Guid.NewGuid(), [item]);
        batch = invalid switch
        {
            0 => batch with { PreviewId = Guid.Empty },
            1 => batch with { Records = default },
            2 => batch with { Records = [] },
            3 => batch with { Records = Enumerable.Range(0, 51).Select(_ => Inspection()).ToImmutableArray() },
            4 => batch with { Records = [null!] },
            5 => batch with { Records = [item with { StoreIdentity = "" }] },
            6 => batch with { Records = [item, item] },
            7 => batch with { Records = [item, Inspection() with { StoreIdentity = "foreign" }] },
            8 => batch with { Records = [item with { Grant = item.Grant with { Status = OperationGrantStatus.Consumed } }] },
            _ => batch with { Records = [item with { Grant = item.Grant with { Status = OperationGrantStatus.Revoked } }] },
        };
        var validate = batch.Validate;
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FiftyCompleteRecordsStillMustFitTheByteBoundAndDecisionPreservesTheWholeRetainedSet()
    {
        var records = Enumerable.Range(0, 50).Select(_ => Inspection()).ToImmutableArray();
        var batch = new ExactGrantBatch(Guid.NewGuid(), records);
        batch.Records.Should().HaveCount(ExactGrantPage.MaximumRecords);
        var validate = batch.Validate;
        validate.Should().Throw<InvalidDataException>().WithMessage("*64 KiB*");
        var revoked = records.Take(2).Select(item => item.Grant.Revoke("explicit-user-revocation")).ToImmutableArray();
        var result = new ExactGrantBatchDecision(HostInteractionOutcome.Revoked, "explicit-user-revocation", revoked);
        result.Outcome.Should().Be(HostInteractionOutcome.Revoked);
        result.Reason.Should().Be("explicit-user-revocation");
        result.RevokedGrants.Should().Equal(revoked);
    }

    private static ExactGrantInspection Inspection()
    {
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, new(Guid.NewGuid()));
        var digest = new string('a', 64);
        var binding = new ExactOperationBinding("bounded.read", "builtin", "inspect", digest, digest, digest,
            digest, digest, digest, digest, digest, digest, new(1));
        var grant = new OperationGrant(new(Guid.NewGuid()), new(1),
            new(request, new(Guid.NewGuid()), new(1), binding, HostOperationEffect.BoundedRead, DateTimeOffset.UnixEpoch),
            OperationGrantScope.Perpetual, new(1), RequestOrigin.LocalUi, DateTimeOffset.UnixEpoch);
        return new("store", grant, null, false);
    }
}