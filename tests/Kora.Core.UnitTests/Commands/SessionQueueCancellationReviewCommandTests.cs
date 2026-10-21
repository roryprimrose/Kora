using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueueCancellationReviewCommandTests
{
    private const string Session = "abcdef01-2345-6789-abcd-012345678901";
    private const string TaskId = "bcdef012-3456-7890-abcd-012345678901";
    private const string Prefix = "session queue cancellation review ";

    [Fact]
    public void EveryBoundedSubsetAndInputOrderIsPreservedForLaterAuthoritativeResolution()
    {
        var ids = Enumerable.Range(1, SessionQueueLimits.MaximumPendingPerSession)
            .Select(index => index.ToString("D8", CultureInfo.InvariantCulture) + "-abcd-0000-0000-000000000000").ToArray();
        for (var mask = 1; mask < 1 << ids.Length; mask++)
        {
            var subset = ids.Where((_, index) => (mask & 1 << index) != 0).Reverse().ToArray();
            var input = Prefix + Session + " " + string.Join(',', subset);
            var command = SessionQueueCancellationReviewCommand.Parse(input, "Nova")!;
            command.Error.Should().BeNull();
            command.IsHelp.Should().BeFalse();
            command.SessionId.Should().Be(Guid.Parse(Session));
            command.TaskIds.Should().Equal(subset.Select(Guid.Parse));
            SessionQueueCancellationReviewCommand.Parse("Nova, " + input.ToUpperInvariant()
                .Replace(Session.ToUpperInvariant(), Session, StringComparison.Ordinal)
                .Replace(string.Join(',', subset).ToUpperInvariant(), string.Join(',', subset), StringComparison.Ordinal), "Nova")
                .Should().BeEquivalentTo(command);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("review")]
    [InlineData("review " + Session)]
    [InlineData("review " + Session + " " + TaskId + ",")]
    [InlineData("review " + Session + " ," + TaskId)]
    [InlineData("review " + Session + " " + TaskId + ",," + TaskId)]
    [InlineData("review " + Session + " " + TaskId + "," + TaskId)]
    [InlineData("review " + Session + " " + TaskId + " extra")]
    [InlineData("review " + Session + " " + TaskId + ":1")]
    [InlineData("confirm " + Session + " " + TaskId)]
    [InlineData("discard " + Session)]
    [InlineData("help extra")]
    [InlineData("review latest " + TaskId)]
    [InlineData("review current " + TaskId)]
    [InlineData("review \"name\" " + TaskId)]
    [InlineData("review " + Session + " all")]
    [InlineData("review " + Session + " " + "BCDEF012-3456-7890-abcd-012345678901")]
    [InlineData("review ABCDEF01-2345-6789-abcd-012345678901 " + TaskId)]
    [InlineData("review 00000000-0000-0000-0000-000000000000 " + TaskId)]
    [InlineData("review " + Session + " 00000000-0000-0000-0000-000000000000")]
    [InlineData("review {" + Session + "} " + TaskId)]
    [InlineData("review " + Session + " {" + TaskId + "}")]
    [InlineData("review\t" + Session + " " + TaskId)]
    public void ReservedFamilyRefusesMalformedOrAuthorityShapedInput(string tail) =>
        SessionQueueCancellationReviewCommand.Parse("session queue cancellation " + tail, "Kora")!.Error.Should().NotBeNull();

    [Theory]
    [InlineData("session queue")]
    [InlineData("session queue other")]
    [InlineData("session queue\ncancellation help")]
    public void IncompleteFamilyIsReserved(string input) =>
        SessionQueueCancellationReviewCommand.Parse(input, "Kora")!.Error.Should().NotBeNull();

    [Theory]
    [InlineData("session")]
    [InlineData("session help")]
    [InlineData("session conversation help")]
    [InlineData("queue cancel-selected-review")]
    [InlineData("sessionx queue cancellation help")]
    [InlineData("Kora session queue cancellation help")]
    public void UnrelatedAndOldNameInputsDoNotStealExistingFamilies(string input) =>
        SessionQueueCancellationReviewCommand.Parse(input, "Nova").Should().BeNull();

    [Fact]
    public void ExplicitHelpAndBoundsNeverDefaultToAll()
    {
        SessionQueueCancellationReviewCommand.Parse("Nova session queue cancellation HELP", "Nova")!.IsHelp.Should().BeTrue();
        SessionQueueCancellationReviewCommand.Parse("session queue cancellation help", "Nova")!.TaskIds.Should().BeEmpty();
        SessionQueueCancellationReviewCommand.Parse(Prefix + Session + " " + TaskId + new string(' ', 1024), "Nova")!.Error.Should().NotBeNull();
        var ids = Enumerable.Range(1, SessionQueueLimits.MaximumPendingPerSession + 1)
            .Select(index => index.ToString("D8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");
        SessionQueueCancellationReviewCommand.Parse(Prefix + Session + " " + string.Join(',', ids), "Nova")!.Error.Should().NotBeNull();
        SessionCommand.Syntax.Should().Contain(SessionQueueCancellationReviewCommand.Syntax);
    }
}
