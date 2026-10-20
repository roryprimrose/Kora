using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueuePrerequisitePrepareCommandTests
{
    private const string Id = "12345678-1234-1234-abcd-123456789012";
    private const string TaskId = "87654321-1234-1234-abcd-123456789012";
    private const string Head = "session queue prerequisite ";
    private const string Valid = Head + "prepare " + Id + " " + TaskId;

    [Theory]
    [InlineData(Valid, "Kora")]
    [InlineData("Nova, " + Valid, "Nova")]
    [InlineData("nova " + Valid, "Nova")]
    [InlineData(" SESSION  QUEUE PREREQUISITE PREPARE " + Id + " " + TaskId + " ", "Kora")]
    public void ExactTaskUsesOnlyTheExplicitCanonicalIds(string input, string name) =>
        SessionQueuePrerequisitePrepareCommand.Parse(input, name).Should()
            .Be(new SessionQueuePrerequisitePrepareCommand(Guid.Parse(Id), Guid.Parse(TaskId)));

    [Fact]
    public void NoneAndHelpAreExplicitDistinctRoutes()
    {
        SessionQueuePrerequisitePrepareCommand.Parse(Head + "prepare " + Id + " none", "Kora").Should()
            .Be(new SessionQueuePrerequisitePrepareCommand(Guid.Parse(Id), null));
        SessionQueuePrerequisitePrepareCommand.Parse(Head + "help", "Kora").Should()
            .Be(new SessionQueuePrerequisitePrepareCommand(Guid.Empty, null, Help: true));
        SessionQueuePrerequisitePrepareCommand.Parse(Head + "HELP ", "Kora")!.Help.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("session")]
    [InlineData("session queue")]
    [InlineData("queue prerequisite prepare")]
    [InlineData("task queue prerequisite")]
    [InlineData("session request help")]
    [InlineData("session queue cancellation review")]
    [InlineData("session queue replacement review")]
    [InlineData("session queue move review")]
    [InlineData("session queue prerequisites")]
    [InlineData("Kora")]
    [InlineData("KoraX, " + Valid)]
    [InlineData("Kora:" + Valid)]
    [InlineData("Nova, " + Valid)]
    public void UnrelatedAndSiblingFamiliesAreNotClaimed(string input) =>
        SessionQueuePrerequisitePrepareCommand.Parse(input, "Kora").Should().BeNull();

    [Theory]
    [InlineData(Head)]
    [InlineData(Head + "prepare")]
    [InlineData(Head + "prepare " + Id)]
    [InlineData(Head + "prepare " + Id + " None")]
    [InlineData(Head + "prepare " + Id + " NONE")]
    [InlineData(Head + "prepare " + Id + " latest")]
    [InlineData(Head + "prepare selected none")]
    [InlineData(Head + "prepare \"name\" none")]
    [InlineData(Head + "prepare 00000000-0000-0000-0000-000000000000 none")]
    [InlineData(Head + "prepare 12345678-1234-1234-ABCD-123456789012 none")]
    [InlineData(Head + "prepare {12345678-1234-1234-abcd-123456789012} none")]
    [InlineData(Head + "prepare 1234567812341234abcd123456789012 none")]
    [InlineData(Head + "prepare " + Id + " 87654321-1234-1234-ABCD-123456789012")]
    [InlineData(Head + "prepare " + Id + " 00000000-0000-0000-0000-000000000000")]
    [InlineData(Head + "prepare " + Id + " nonsense")]
    [InlineData(Head + "apply " + Id + " none")]
    [InlineData(Head + "help " + Id)]
    [InlineData(Valid + " extra")]
    [InlineData(Valid + "\n")]
    [InlineData("session\tqueue prerequisite help")]
    public void MalformedFamilyIsReservedWithoutFallbackOrImplicitNone(string input)
    {
        var parsed = SessionQueuePrerequisitePrepareCommand.Parse(input, "Kora")!;
        parsed.Error.Should().Contain(SessionQueuePrerequisitePrepareCommand.Syntax).And.Contain("PREPARATION");
        parsed.SessionId.Should().BeEmpty();
        parsed.Help.Should().BeFalse();
    }

    [Fact]
    public void LimitsCountOriginalUtf8BytesWithoutTruncation()
    {
        SessionQueuePrerequisitePrepareCommand.Parse(Valid.PadRight(1024), "Kora")!.Error.Should().BeNull();
        SessionQueuePrerequisitePrepareCommand.Parse(Valid.PadRight(1025), "Kora")!.Error.Should().NotBeNull();
        SessionQueuePrerequisitePrepareCommand.Parse(Valid + new string('\u00e9', 512), "Kora")!.Error.Should().NotBeNull();
    }
}
