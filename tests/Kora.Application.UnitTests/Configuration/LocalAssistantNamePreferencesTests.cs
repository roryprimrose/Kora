using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalAssistantNamePreferencesTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void Load_returns_null_when_the_preference_does_not_exist()
    {
        CreatePreferences().LoadName().Should().BeNull();
    }

    [Fact]
    public void Save_atomically_replaces_and_loads_the_normalized_name()
    {
        var preferences = CreatePreferences();

        preferences.SaveName(" First  Name ");
        preferences.SaveName("Nova");

        preferences.LoadName().Should().Be("Nova");
        File.Exists(Path.Combine(root, "Preferences", "assistant-name.tmp")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Nova!")]
    public void Save_rejects_invalid_names(string name)
    {
        var action = () => CreatePreferences().SaveName(name);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Load_rejects_an_invalid_saved_name()
    {
        var directory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "assistant-name.txt"), "Nova!");

        var action = CreatePreferences().LoadName;

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*assistant name*");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalAssistantNamePreferences CreatePreferences() =>
        new(
            new TestPaths(root, Path.Combine(root, "Roaming")),
            NullLogger<LocalAssistantNamePreferences>.Instance);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
