using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Communication;

namespace Kora.Application.UnitTests.Configuration;

public sealed class CommunicationActivityCommandTests
{
    [Theory]
    [InlineData("list call observation settings", AppearanceCommandOperation.List, CommunicationActivitySelection.None)]
    [InlineData("get calls.activity-source", AppearanceCommandOperation.Get, CommunicationActivitySelection.None)]
    [InlineData("status calls.activity-source", AppearanceCommandOperation.Get, CommunicationActivitySelection.None)]
    [InlineData("reset calls.activity-source", AppearanceCommandOperation.Reset, CommunicationActivitySelection.None)]
    [InlineData("set calls.activity-source to None", AppearanceCommandOperation.Set, CommunicationActivitySelection.None)]
    [InlineData("Kora, set calls.activity-source to WindowsCommunicationsAudio", AppearanceCommandOperation.Set, CommunicationActivitySelection.WindowsCommunicationsAudio)]
    [InlineData("Kora set calls.activity-source to invalid", AppearanceCommandOperation.Clarify, CommunicationActivitySelection.None)]
    [InlineData("set calls.activity-source to None\n", AppearanceCommandOperation.Clarify, CommunicationActivitySelection.None)]
    public void Exact_commands_have_bounded_typed_semantics(string text, AppearanceCommandOperation operation, CommunicationActivitySelection selection)
    {
        var command = CommunicationActivityCommand.Parse(text, "Kora")!;
        command.Operation.Should().Be(operation);
        command.Selection.Should().Be(selection);
        CommunicationActivityCommand.Syntax.Should().Contain(CommunicationActivityCommand.OptionId);
    }

    [Theory]
    [InlineData("Kora")]
    [InlineData("Korax set calls.activity-source to None")]
    [InlineData("say set calls.activity-source to None")]
    [InlineData("ordinary request")]
    public void Embedded_or_unrelated_text_is_not_an_invocation(string text) =>
        CommunicationActivityCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void Oversized_reserved_input_clarifies_without_dispatch() =>
        CommunicationActivityCommand.Parse("set calls.activity-source to " + new string('x', 1024), "Kora")!
            .Operation.Should().Be(AppearanceCommandOperation.Clarify);
}