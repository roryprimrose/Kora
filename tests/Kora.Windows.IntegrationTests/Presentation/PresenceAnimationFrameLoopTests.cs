using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed class PresenceAnimationFrameLoopTests
{
    [Fact]
    public void Presence_animation_uses_a_bounded_render_priority_cadence()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Kora", "Controls", "PresenceControl.cs"));
        var loop = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Kora", "Controls", "PresenceRenderLoop.cs"));
        var scheduler = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Kora", "Controls", "DispatcherPresenceFrameScheduler.cs"));
        var window = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Kora", "MainWindow.axaml.cs"));

        loop.Should().Contain("SpeechInterval = TimeSpan.FromMilliseconds(16)")
            .And.Contain("ActiveInterval = TimeSpan.FromMilliseconds(33)")
            .And.Contain("QuietInterval = TimeSpan.FromMilliseconds(198)")
            .And.Contain("admittedGeneration != generation");
        scheduler.Should().Contain("DispatcherTimer.RunOnce")
            .And.Contain("DispatcherPriority.Background");
        source.Should().Contain("IsSpeaking")
            .And.Contain("IsEffectivelyVisible")
            .And.Contain("isAnimationEnabled")
            .And.Contain("step / PresenceRenderLoop.ActiveInterval.TotalMilliseconds")
            .And.Contain(".OrderBy(index => particles[index].Z)")
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
