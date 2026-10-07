using System.Text;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionCommandTests
{
    private const string Id = "12345678-1234-1234-1234-123456789012";

    [Theory]
    [InlineData("session help", SessionCommandOperation.Help)]
    [InlineData("SESSION HELP", SessionCommandOperation.Help)]
    [InlineData("Nova, session list", SessionCommandOperation.List)]
    [InlineData("Nova session status " + Id, SessionCommandOperation.Status)]
    [InlineData("session inspect " + Id, SessionCommandOperation.Inspect)]
    [InlineData("session create \"Private café \U0001F600\"", SessionCommandOperation.Create)]
    [InlineData("session create \"A \"\"quoted\"\" name\"", SessionCommandOperation.Create)]
    [InlineData("session rename " + Id + " 1 0 \"Same name\"", SessionCommandOperation.Rename)]
    [InlineData("session done " + Id + " 9223372036854775807", SessionCommandOperation.Done)]
    [InlineData("session resume " + Id + " 2", SessionCommandOperation.Resume)]
    public void Exact_commands_parse_without_inference(string input, SessionCommandOperation operation)
    {
        SessionCommand.Parse(input, "Nova")!.Operation.Should().Be(operation);
    }

    [Theory]
    [InlineData("session list after " + Id + " limit 1", SessionCommandOperation.List, "tasks", 1)]
    [InlineData("session list limit 50", SessionCommandOperation.List, "tasks", 50)]
    [InlineData("session inspect " + Id + " tasks after " + Id + " limit 25", SessionCommandOperation.Inspect, "tasks", 25)]
    [InlineData("session inspect " + Id + " questions limit 50", SessionCommandOperation.Inspect, "questions", 50)]
    [InlineData("SESSION INSPECT " + Id + " QUESTIONS LIMIT 50", SessionCommandOperation.Inspect, "questions", 50)]
    public void Pages_have_exact_cursors_and_bounded_typed_shape(string input, SessionCommandOperation operation, string kind, int limit)
    {
        var command = SessionCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(operation);
        command.PageKind.Should().Be(kind);
        command.Limit.Should().Be(limit);
        command.After.Should().Be(input.Contains("after", StringComparison.Ordinal) ? Guid.Parse(Id) : null);
    }

    [Theory]
    [InlineData("sessions")]
    [InlineData("session.lock")]
    [InlineData("/lock")]
    [InlineData("sessionx help")]
    [InlineData("show status")]
    public void Unrelated_routes_remain_unclaimed(string input) => SessionCommand.Parse(input, "Kora").Should().BeNull();

    [Theory]
    [InlineData("session")]
    [InlineData("session\tlist")]
    [InlineData("session\nlist")]
    [InlineData("session help extra")]
    [InlineData("session help \"name\"")]
    [InlineData("session create")]
    [InlineData("session create \"\"")]
    [InlineData("session create \" leading\"")]
    [InlineData("session create \"e\u0301\"")]
    [InlineData("session create \"a\u200Bb\"")]
    [InlineData("session create \"unterminated")]
    [InlineData("session create \"name\" extra")]
    [InlineData("session create\"name\"")]
    [InlineData("session create \"name\" \"other\"")]
    [InlineData("session delete " + Id)]
    [InlineData("session status selected")]
    [InlineData("session status 00000000-0000-0000-0000-000000000000")]
    [InlineData("session status {12345678-1234-1234-1234-123456789012}")]
    [InlineData("session status " + Id + " extra")]
    [InlineData("session done " + Id)]
    [InlineData("session done " + Id + " 0")]
    [InlineData("session done " + Id + " -1")]
    [InlineData("session done " + Id + " +1")]
    [InlineData("session done " + Id + " 9223372036854775808")]
    [InlineData("session done " + Id + " 1 \"name\"")]
    [InlineData("session rename " + Id + " 1 -1 \"name\"")]
    [InlineData("session rename " + Id + " 1 1")]
    [InlineData("session rename " + Id + " 1 1 extra \"name\"")]
    [InlineData("session list after")]
    [InlineData("session list after invalid")]
    [InlineData("session list limit")]
    [InlineData("session list limit 0")]
    [InlineData("session list limit 51")]
    [InlineData("session list limit nope")]
    [InlineData("session list limit 5 after " + Id)]
    [InlineData("session list unknown")]
    [InlineData("session list \"name\"")]
    [InlineData("session inspect " + Id + " approvals")]
    [InlineData("session status \"title\"")]
    [InlineData("session create \"multi\nline\"")]
    public void Invalid_or_unsupported_session_input_is_claimed_and_fails_closed(string input)
    {
        var command = SessionCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.Invalid);
        command.Error.Should().Contain(SessionCommand.Syntax);
    }

    [Fact]
    public void Quoting_unicode_and_whole_input_limits_are_exact()
    {
        var name = new string('x', 120);
        var accepted = SessionCommand.Parse("session create \"" + name + "\"", "Kora")!;
        accepted.Name!.Value.Should().Be(name);
        SessionCommand.Parse("session create \"" + name + "x\"", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session create \"\uD800\"", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("session create \"A \"\"quoted\"\" name\"", "Kora")!.Name!.Value.Should().Be("A \"quoted\" name");
        var input = "session help" + new string(' ', SessionCommand.MaximumInputBytes - 12);
        Encoding.UTF8.GetByteCount(input).Should().Be(SessionCommand.MaximumInputBytes);
        SessionCommand.Parse(input, "Kora")!.Operation.Should().Be(SessionCommandOperation.Help);
        SessionCommand.Parse(input + " ", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.DiscoveryPhrases.Should().Equal("session help", "session list");
    }

    [Fact]
    public void Complete_serialized_result_has_exact_utf8_bounds_and_truthful_typed_fields()
    {
        var empty = new SessionCommandResult("observed", "");
        var overhead = SessionCommandResult.Serialize(empty).Length;
        var exact = empty with { Message = new string('x', SessionCommand.MaximumResultBytes - overhead) };
        SessionCommandResult.Serialize(exact).Length.Should().Be(SessionCommand.MaximumResultBytes);
        var overflow = () => SessionCommandResult.Serialize(exact with { Message = exact.Message + "x" });
        overflow.Should().Throw<InvalidDataException>().WithMessage("*smaller explicit page*");
        var result = new SessionCommandResult("observed", "No execution inferred.")
        {
            Sessions = [new(Guid.Parse(Id), true, 3, 2, "User label")],
            Tasks = [new(Guid.NewGuid(), Guid.NewGuid(), HostTaskState.Unknown, 4, RequestOrigin.ActivatedVoice)],
            Questions = [new(Guid.NewGuid(), 2, "Pending")],
            Next = Guid.Parse(Id),
        };
        Encoding.UTF8.GetString(SessionCommandResult.Serialize(result)).Should().Contain("\"generation\":3")
            .And.Contain("\"metadataRevision\":2").And.Contain("\"next\":\"" + Id + "\"");
    }
}
