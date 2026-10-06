using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.Configuration;

public sealed class LocalModelExecutionPreferencesTests
{
    [Fact]
    public void Missing_preferences_enable_local_models_and_disable_hosted_models()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kora-model-execution-{Guid.NewGuid():N}");
        var preferences = new LocalModelExecutionPreferences(new TestPaths(path));

        preferences.Load().Should().Be(ModelExecutionSettings.Default);
        Directory.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void Model_execution_settings_survive_a_new_instance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kora-model-execution-{Guid.NewGuid():N}");
        try
        {
            new LocalModelExecutionPreferences(new TestPaths(path)).Save(new(
                LocalModelsEnabled: false,
                HostedModelsEnabled: true));

            new LocalModelExecutionPreferences(new TestPaths(path)).Load().Should().Be(new ModelExecutionSettings(
                LocalModelsEnabled: false,
                HostedModelsEnabled: true));
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("""{"Version":2,"LocalModelsEnabled":true,"HostedModelsEnabled":false}""")]
    [InlineData("""{"Version":1,"HostedModelsEnabled":false}""")]
    [InlineData("""{"Version":1,"LocalModelsEnabled":true}""")]
    [InlineData("""{"Version":1,"LocalModelsEnabled":"true","HostedModelsEnabled":false}""")]
    [InlineData("""{"Version":1,"LocalModelsEnabled":true,"HostedModelsEnabled":null}""")]
    [InlineData("""{"Version":1.5,"LocalModelsEnabled":true,"HostedModelsEnabled":false}""")]
    [InlineData("""{"LocalModelsEnabled":true,"HostedModelsEnabled":false}""")]
    [InlineData("""{"Version":1,"LocalModelsEnabled":true,"HostedModelsEnabled":false""")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Invalid_or_newer_settings_are_never_accepted(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kora-model-execution-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(path, "Preferences"));
            File.WriteAllText(
                Path.Combine(path, "Preferences", "model-execution-settings.json"),
                json);

            var action = () => new LocalModelExecutionPreferences(new TestPaths(path)).Load();

            action.Should().Throw<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void Save_rejects_null_settings_without_creating_storage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kora-model-execution-{Guid.NewGuid():N}");
        var preferences = new LocalModelExecutionPreferences(new TestPaths(path));

        var action = () => preferences.Save(null!);

        action.Should().Throw<ArgumentNullException>();
        Directory.Exists(path).Should().BeFalse();
    }

    private sealed record TestPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}
