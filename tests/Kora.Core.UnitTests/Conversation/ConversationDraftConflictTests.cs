using AwesomeAssertions;

using Kora.Core.Conversation;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationDraftConflictTests
{
    [Fact]
    public void ExactPrewriteRejectionIsTypedAndRetainsExistingRecoveryMessage()
    {
        var conflict = new ConversationDraftConflictException();
        conflict.Should().BeAssignableTo<InvalidOperationException>();
        conflict.Message.Should().Be("Review the exact Active session, saved draft body and revision before saving or clearing.");
    }
}
