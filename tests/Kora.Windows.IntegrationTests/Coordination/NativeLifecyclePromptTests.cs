using AwesomeAssertions;
using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests.Coordination;

public sealed class NativeLifecyclePromptTests
{
    [Fact]
    public void Ownership_questions_retain_yes_no_and_default_no()
    {
        var flags = NativeLifecyclePrompt.GetDisplayFlags(isQuestion: true);

        (flags & 0xF).Should().Be(4);
        (flags & 0xF00).Should().Be(0x100);
    }

    [Fact]
    public void Failure_notices_have_only_ok_and_cannot_look_like_handoff_approval()
    {
        var flags = NativeLifecyclePrompt.GetDisplayFlags(isQuestion: false);

        (flags & 0xF).Should().Be(0);
        (flags & 0xF00).Should().Be(0);
        (flags & 0xF0).Should().Be(0x10);
    }
}
