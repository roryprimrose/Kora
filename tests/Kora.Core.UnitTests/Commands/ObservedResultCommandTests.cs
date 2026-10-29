using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class ObservedResultCommandTests
{
    [Theory]
    [InlineData("open observed result application.get_version message 1 result 1", WorkspaceSurfaceTarget.ObservedResult)]
    [InlineData("Kora open observed result application get version message 1 result 1", WorkspaceSurfaceTarget.ObservedResult)]
    [InlineData("Kora open observed result application.get_version", WorkspaceSurfaceTarget.ObservedResult)]
    [InlineData("open observed result", WorkspaceSurfaceTarget.Invalid)]
    [InlineData("open observed result ", WorkspaceSurfaceTarget.Invalid)]
    [InlineData("open observed result version\n", WorkspaceSurfaceTarget.Invalid)]
    [InlineData("open observed results", WorkspaceSurfaceTarget.Invalid)]
    public void DeliberateNamedCommandNeverUsesGenericYesOrUnboundedInput(string text, WorkspaceSurfaceTarget target)
    {
        WorkspaceSurfaceCommand.Parse(text, "Kora")!.Target.Should().Be(target);
        WorkspaceSurfaceCommand.Parse("yes", "Kora").Should().BeNull();
        WorkspaceSurfaceCommand.Parse("open observed result " + new string('a', 256), "Kora")!.Target.Should().Be(WorkspaceSurfaceTarget.Invalid);
        WorkspaceSurfaceCommand.MatchesObservationName("application get version message 1 result 1",
            "application.get_version message 1 result 1").Should().BeTrue();
        WorkspaceSurfaceCommand.MatchesObservationName("application get version message 2 result 1",
            "application.get_version message 1 result 1").Should().BeFalse();
    }
}
