using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Definitions.Artifacts;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

public sealed class ArtifactCatalogueReloadServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "Kora.Windows.IntegrationTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Disk_change_publishes_new_revision_and_invalid_change_keeps_last_valid_catalogue()
    {
        var prompts = Path.Combine(root, "prompts");
        Directory.CreateDirectory(prompts);
        var prompt = Path.Combine(prompts, "explain.prompt.md");
        WritePrompt(prompt, "Original", "Original instructions.");
        var paths = new Paths(Path.Combine(root, "kora"));
        var preferences = new Preferences(new([], [], [prompts], [], [], []));
        var configuration = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var mcp = new McpServerConfigurationService(paths, preferences, Path.Combine(root, "profile"));
        var embedded = EmbeddedArtifactCatalogue.Load();
        var disk = new WindowsDiskArtifactDiscovery(paths, preferences.Load(), Path.Combine(root, "profile")).Load();
        var catalogue = new ArtifactCatalogue([.. embedded.Artifacts, .. disk]);
        using var reload = new ArtifactCatalogueReloadService(
            catalogue,
            paths,
            preferences,
            configuration,
            mcp,
            NullLogger<ArtifactCatalogueReloadService>.Instance,
            profileRoamingRoot: Path.Combine(root, "profile"));
        reload.Start();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        catalogue.Changed += (_, _) => changed.TrySetResult();
        catalogue.ReloadFailed += (_, args) => changed.TrySetException(args.Exception);

        WritePrompt(prompt, "Updated", "Updated instructions.");
        await changed.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);

        var updated = catalogue.Artifacts.Single(artifact =>
            string.Equals(artifact.CommandName, "updated", StringComparison.Ordinal));
        updated.Content.Should().Be("Updated instructions.");
        var updatedDigest = updated.Digest;

        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalogue.ReloadFailed += (_, args) => failed.TrySetResult(args.Exception);
        File.WriteAllText(prompt, "invalid");
        (await failed.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken)).Should().BeOfType<InvalidDataException>();
        catalogue.Artifacts.Single(artifact =>
                string.Equals(artifact.CommandName, "updated", StringComparison.Ordinal))
            .Digest.Should().Be(updatedDigest);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WritePrompt(string path, string name, string body) =>
        File.WriteAllText(path, $"""
            ---
            name: "{name}"
            description: "Explain a result."
            ---
            {body}
            """);

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }

    private sealed class Preferences(ArtifactDirectorySettings initialSettings) : IArtifactDirectoryPreferences
    {
        private ArtifactDirectorySettings settings = initialSettings;
        public ArtifactDirectorySettings Load() => settings;
        public void Save(ArtifactDirectorySettings value) => settings = value;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
