using AwesomeAssertions;
using Kora.Application.Tools;

namespace Kora.Application.UnitTests.Tools;

public sealed class SessionMetadataCommandTests
{
    [Fact]
    public void Original_question_and_exact_ids_are_preserved_and_bad_grammar_never_becomes_a_generic_request()
    {
        var command = SessionMetadataCommand.Parse(
            "Kora, review session metadata 10000000-0000-0000-0000-000000000001 for 10000000-0000-0000-0000-000000000002: What work and observed expiry?", "Kora");
        command!.Error.Should().BeNull();
        command.Question.Should().Be("What work and observed expiry?");
        command.Subject.Should().EndWith("001");
        command.Caller.Should().EndWith("002");
        SessionMetadataCommand.Parse("review session metadata", "Kora")!.Error.Should().NotBeNull();
        SessionMetadataCommand.Parse("review session metadata bad for bad: q", "Kora")!.Error.Should().NotBeNull();
        SessionMetadataCommand.Parse("ordinary", "Kora").Should().BeNull();
    }
}
