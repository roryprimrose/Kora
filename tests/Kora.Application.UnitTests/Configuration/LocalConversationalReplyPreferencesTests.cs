using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalConversationalReplyPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "reply-" + Guid.NewGuid().ToString("N"));
    private LocalConversationalReplyPreferences Create() => new(new Paths(root, root));
    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;

    [Fact]
    public void Default_absent_and_explicit_choices_preserve_the_atomic_evidence_hold_across_restart()
    {
        var preferences = Create();
        preferences.Load().Should().BeNull();
        foreach (var enabled in new[] { true, false })
        {
            preferences.BeginWrite();
            preferences.Save(enabled);
            preferences.ReadBack().Should().Be(enabled);
            Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            Create().Load().Should().Be(enabled);
        }
        Directory.GetFiles(Path.Combine(root, "Preferences")).Should().ContainSingle();
        File.ReadAllText(Path.Combine(root, "Preferences", "conversational-replies.txt")).Should().Be("Off");
    }

    [Theory]
    [InlineData("")]
    [InlineData("on")]
    [InlineData("True")]
    [InlineData("On\n")]
    public void Malformed_saved_values_are_not_reinterpreted_as_off(string content)
    {
        Directory.CreateDirectory(Path.Combine(root, "Preferences"));
        File.WriteAllText(Path.Combine(root, "Preferences", "conversational-replies.txt"), content);
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
}
