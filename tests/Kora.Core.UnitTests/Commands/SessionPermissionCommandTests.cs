using AwesomeAssertions;

using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionPermissionCommandTests
{
    private const string Id = "12345678-abcd-1234-1234-123456789012";

    [Theory]
    [InlineData("session permission help", SessionCommandOperation.Help)]
    [InlineData("Kora, SESSION PERMISSION STATUS " + Id, SessionCommandOperation.PermissionStatus)]
    [InlineData("session permission confirm " + Id, SessionCommandOperation.PermissionConfirm)]
    [InlineData("session permission cancel " + Id, SessionCommandOperation.PermissionCancel)]
    public void Exact_permission_commands_are_reserved(string input, SessionCommandOperation operation)
    {
        var command = SessionCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(operation);
        command.IsPermissionCommand.Should().Be(operation != SessionCommandOperation.Help);
        if (operation != SessionCommandOperation.Help) { command.SessionId.Should().Be(Guid.Parse(Id)); }
    }

    [Theory]
    [InlineData("review-all", SessionPermissionLevel.ReviewAll)]
    [InlineData("MODEL-REVIEW", SessionPermissionLevel.ModelReview)]
    [InlineData("approve-all", SessionPermissionLevel.ApproveAll)]
    public void Review_has_one_exact_typed_level(string input, SessionPermissionLevel level)
    {
        var command = SessionCommand.Parse("session permission review " + Id + " " + input, "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.PermissionReview);
        command.PermissionLevel.Should().Be(level);
        command.IsPermissionCommand.Should().BeTrue();
    }

    [Theory]
    [InlineData("session permission")]
    [InlineData("session permission review " + Id)]
    [InlineData("session permission review " + Id + " 2")]
    [InlineData("session permission review " + Id + " always")]
    [InlineData("session permission confirm selected")]
    [InlineData("session permission status 12345678-ABCD-1234-1234-123456789012")]
    [InlineData("session permission status 00000000-0000-0000-0000-000000000000")]
    [InlineData("session permission status " + Id + " approve-all")]
    [InlineData("session permission confirm " + Id + " approve-all")]
    [InlineData("session permission unknown " + Id)]
    [InlineData("session permission review " + Id + " approve-all extra")]
    [InlineData("session permission review " + Id + " approve-all\n")]
    public void Invalid_permission_input_never_falls_through_to_inference(string input)
    {
        var command = SessionCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.Invalid);
        command.Error.Should().Be(SessionCommand.PermissionSyntax);
    }

    [Fact]
    public void Complete_input_bound_includes_activation_and_whitespace()
    {
        var text = "Kora, session permission status " + Id;
        var exact = text + new string(' ', SessionCommand.MaximumInputBytes - text.Length);
        SessionCommand.Parse(exact, "Kora")!.Operation.Should().Be(SessionCommandOperation.PermissionStatus);
        SessionCommand.Parse(exact + " ", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Syntax.Should().Contain(SessionCommand.PermissionSyntax);
    }
}
