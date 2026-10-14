using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Definitions.Artifacts;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

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

    [Fact]
    public async Task Deferred_reload_does_not_reuse_the_starting_activity()
    {
        var prompts = Path.Combine(root, "prompts");
        Directory.CreateDirectory(prompts);
        var prompt = Path.Combine(prompts, "explain.prompt.md");
        WritePrompt(prompt, "Original", "Original instructions.");
        var paths = new Paths(Path.Combine(root, "kora"));
        var preferences = new Preferences(new([], [], [prompts], [], [], []));
        var configuration = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var logger = new ActivityCapturingLogger();
        using var reload = new ArtifactCatalogueReloadService(
            new ArtifactCatalogue(EmbeddedArtifactCatalogue.Load().Artifacts),
            paths,
            preferences,
            configuration,
            new McpServerConfigurationService(paths, preferences, Path.Combine(root, "profile")),
            logger,
            profileRoamingRoot: Path.Combine(root, "profile"));
        using (var activity = new Activity("starting-request").Start())
        {
            reload.Start();
        }

        WritePrompt(prompt, "Updated", "Updated instructions.");

        (await logger.ReloadActivity.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Unrelated_change_under_a_missing_file_fallback_ancestor_does_not_reload()
    {
        var paths = new Paths(Path.Combine(root, "kora"));
        Directory.CreateDirectory(paths.LocalRoot);
        var preferences = new Preferences(new([], [], [], [], [], []));
        var logger = new ActivityCapturingLogger();
        using var reload = new ArtifactCatalogueReloadService(
            new ArtifactCatalogue(EmbeddedArtifactCatalogue.Load().Artifacts),
            paths,
            preferences,
            new ArtifactDirectoryConfigurationService(preferences, new Audit()),
            new McpServerConfigurationService(paths, preferences, Path.Combine(root, "profile")),
            logger,
            profileRoamingRoot: Path.Combine(root, "profile"));
        reload.Start();

        var logs = Path.Combine(paths.LocalRoot, "Logs");
        Directory.CreateDirectory(logs);
        await File.WriteAllTextAsync(
            Path.Combine(logs, "kora.log"),
            "unrelated",
            TestContext.Current.CancellationToken);
        await Task.Delay(
            TimeSpan.FromMilliseconds(700),
            TestContext.Current.CancellationToken);

        logger.ReloadActivity.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Unrelated_profile_state_change_under_an_existing_root_does_not_reload()
    {
        var profile = Path.Combine(root, "profile");
        var profiles = Path.Combine(profile, "Code", "User", "profiles");
        var profileState = Path.Combine(profiles, "profile-one");
        Directory.CreateDirectory(profileState);
        var paths = new Paths(Path.Combine(root, "kora"));
        var preferences = new Preferences(new([], [], [], [], [], []));
        var logger = new ActivityCapturingLogger();
        using var reload = new ArtifactCatalogueReloadService(
            new ArtifactCatalogue(EmbeddedArtifactCatalogue.Load().Artifacts),
            paths,
            preferences,
            new ArtifactDirectoryConfigurationService(preferences, new Audit()),
            new McpServerConfigurationService(paths, preferences, profile),
            logger,
            profileRoamingRoot: profile);
        reload.Start();

        await File.WriteAllTextAsync(
            Path.Combine(profileState, "storage.json"),
            "{}",
            TestContext.Current.CancellationToken);
        await Task.Delay(
            TimeSpan.FromMilliseconds(700),
            TestContext.Current.CancellationToken);

        logger.ReloadActivity.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Reload_logging_does_not_hold_the_configuration_change_gate()
    {
        var paths = new Paths(Path.Combine(root, "kora"));
        Directory.CreateDirectory(paths.LocalRoot);
        var preferences = new Preferences(new([], [], [], [], [], []));
        var configuration = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var logger = new ConfigurationLoadingLogger(configuration);
        using var reload = new ArtifactCatalogueReloadService(
            new ArtifactCatalogue(EmbeddedArtifactCatalogue.Load().Artifacts),
            paths,
            preferences,
            configuration,
            new McpServerConfigurationService(paths, preferences, Path.Combine(root, "profile")),
            logger,
            profileRoamingRoot: Path.Combine(root, "profile"));
        reload.Start();

        reload.ReloadNow().Should().BeTrue();
        await logger.Load!.WaitAsync(TestContext.Current.CancellationToken);

        logger.CompletedDuringLog.Should().BeTrue();
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

    private sealed class ActivityCapturingLogger : ILogger<ArtifactCatalogueReloadService>
    {
        internal TaskCompletionSource<Activity?> ReloadActivity { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 2600)
            {
                ReloadActivity.TrySetResult(Activity.Current);
            }
        }
    }

    private sealed class ConfigurationLoadingLogger(
        ArtifactDirectoryConfigurationService configuration) : ILogger<ArtifactCatalogueReloadService>
    {
        internal Task<ArtifactDirectorySettings>? Load { get; private set; }
        internal bool CompletedDuringLog { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != 2600)
            {
                return;
            }

            var completed = new ManualResetEventSlim();
            Load = Task.Factory.StartNew(
                () =>
                {
                    var settings = configuration.Load();
                    completed.Set();
                    return settings;
                },
                TestContext.Current.CancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            CompletedDuringLog = completed.Wait(TimeSpan.FromSeconds(2));
        }
    }
}
