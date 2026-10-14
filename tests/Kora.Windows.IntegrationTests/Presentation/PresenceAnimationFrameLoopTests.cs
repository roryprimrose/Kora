using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed class PresenceAnimationFrameLoopTests
{
    [Fact]
    public void Presence_animation_uses_a_bounded_render_priority_cadence()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Kora", "Controls", "PresenceControl.cs"));
        var window = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Kora", "MainWindow.axaml.cs"));

        source.Should().Contain("SpeechFrameInterval = TimeSpan.FromMilliseconds(16)")
            .And.Contain("IsSpeaking")
            .And.Contain("PresenceAnimation.FrameInterval")
            .And.Contain("DispatcherPriority.Background")
            .And.Contain("elapsed.TotalMilliseconds / PresenceAnimation.FrameInterval.TotalMilliseconds")
            .And.Contain("if (!isAttached || !isAnimationEnabled)")
            .And.Contain("if (!IsEffectivelyVisible)")
            .And.Contain(".OrderBy(particle => particle.Z)")
            .And.NotContain("RequestAnimationFrame");
        window.Should().Contain("private void ShowPresenceWindow()")
            .And.Contain("private void HidePresenceWindow()")
            .And.Contain("presence.SetAnimationEnabled(false)")
            .And.Contain("presenceInputTimer.Stop()");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("The repository source root is unavailable.");
    }
}
