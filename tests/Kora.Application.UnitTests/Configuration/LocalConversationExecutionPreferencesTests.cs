using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalConversationExecutionPreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.CurrentDirectory, ".net-test-artifacts", "conversation-limit-" + Guid.NewGuid().ToString("N"));
    private LocalConversationExecutionPreferences Create() => new(new Paths(root, root));
    private sealed record Paths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;

    [Fact]
    public void DefaultAndAtomicWriteHoldSurviveRestart()
    {
        var preferences = Create();
        preferences.Load().Should().BeNull();
        foreach (var limit in new[] { new ConversationExecutionLimit(1), new ConversationExecutionLimit(2) })
        {
            preferences.BeginWrite();
            preferences.Save(limit);
            preferences.ReadBack().Should().Be(limit);
            Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
            preferences.ConfirmWrite();
            Create().Load().Should().Be(limit);
        }
        Directory.GetFiles(Path.Combine(root, "Preferences")).Should().ContainSingle();
    }

    [Fact]
    public void CorruptSavedDemandCannotBecomeQualifiedCapacity()
    {
        Directory.CreateDirectory(Path.Combine(root, "Preferences"));
        File.WriteAllText(Path.Combine(root, "Preferences", "conversation-execution-maximum.txt"), "3");
        Create().Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
}
