using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Conversation;

public sealed class NewRequestCreationAttemptTests
{
    [Fact]
    public void FreshNativeAttemptsHaveIndependentHostOwnedIdentitiesAndGenericNames()
    {
        var first = new NewRequestCreationAttempt();
        var second = new NewRequestCreationAttempt();
        first.Name.Value.Should().Be("New conversation");
        first.Request.Origin.Should().Be(RequestOrigin.LocalUi);
        first.Request.RequestId.Should().NotBe(second.Request.RequestId);
        first.Request.TaskId.Should().NotBe(second.Request.TaskId);
        first.Request.SessionId.Should().NotBe(second.Request.SessionId);
        first.RequireExactTarget(new(new(first.Request.SessionId, new(1), true),
            new(first.Request.SessionId, new(1), first.Name)));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("done")]
    [InlineData("generation")]
    [InlineData("no-metadata")]
    [InlineData("revision")]
    [InlineData("metadata-id")]
    [InlineData("name")]
    public void ConflictingReceiptsCannotBecomeAcceptedFreshTargets(string fault)
    {
        var attempt = new NewRequestCreationAttempt();
        var session = attempt.Request.SessionId;
        var metadata = fault is "no-metadata" ? null : new SessionMetadata(
            fault is "metadata-id" ? new(Guid.NewGuid()) : session,
            new(fault is "revision" ? 2 : 1), fault is "name" ? new("Other") : attempt.Name);
        var target = new SessionWorkspaceEntry(new(fault is "id" ? new(Guid.NewGuid()) : session,
            new(fault is "generation" ? 2 : 1), fault is not "done"), metadata);
        attempt.Invoking(value => value.RequireExactTarget(target)).Should().Throw<InvalidDataException>();
    }
}
