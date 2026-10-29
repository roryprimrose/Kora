using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Definitions.Artifacts;
using Kora.NativeUxFixture;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ArtifactCatalogueReloadServiceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "Kora.Windows.IntegrationTests",
        Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void Saving_an_owned_copy_does_not_restore_a_removed_discovery_directory(ArtifactKind kind)
    {
        var paths = new Paths(Path.Combine(root, "kora"));
        var preferences = new Preferences(new([], [], [], [], [], [], false));
        var catalogue = EmbeddedArtifactCatalogue.Load();
        using var reload = CreateReload(catalogue, paths, preferences, Path.Combine(root, "profile"));
        var document = ArtifactFileDocument.Parse(
            ArtifactFileDocument.Encode("---\nkora-format: 1\nname: Example\ndescription: Example definition\n---\nSaved body.", false),
            kind, "kora-profile", "example");
        var saved = new OwnedInstructionSnapshot("example", "directory", "file", document);
        reload.ReconcileSavedCopy(saved).Should().BeTrue();
        catalogue.Candidates.Should().NotContain(item => string.Equals(item.Id, document.Definition.Id, StringComparison.Ordinal));
        var directory = Path.Combine(paths.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind));
        preferences.Save(new([], kind == ArtifactKind.Instruction ? [directory] : [],
            kind == ArtifactKind.Prompt ? [directory] : [], [], [], [], false));
        reload.ReconcileSavedCopy(saved).Should().BeTrue();
        catalogue.Candidates.Should().Contain(item => string.Equals(item.Id, document.Definition.Id, StringComparison.Ordinal));
        catalogue.Artifacts.Should().NotContain(item => string.Equals(item.Id, document.Definition.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void Removing_a_directory_retires_its_catalogue_entries_even_when_remaining_content_is_invalid()
    {
        var removed = Path.Combine(root, "removed");
        var retained = Path.Combine(root, "retained");
        Directory.CreateDirectory(removed);
        Directory.CreateDirectory(retained);
        var removedFile = Path.Combine(removed, "removed.prompt.md");
        WritePrompt(removedFile, "Removed", "Removed source.");
        var paths = new Paths(Path.Combine(root, "kora"));
        var preferences = new Preferences(new([], [], [removed, retained], [], [], [], false));
        var configuration = new ArtifactDirectoryConfigurationService(preferences, new Audit());
        var catalogue = EmbeddedArtifactCatalogue.Load();
        using var reload = new ArtifactCatalogueReloadService(catalogue, paths, preferences, configuration,
            new McpServerConfigurationService(paths, preferences, Path.Combine(root, "profile")),
            NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: Path.Combine(root, "profile"));
        reload.Start();
        reload.ReloadNow().Should().BeTrue();
        var original = catalogue.Artifacts.Single(item => string.Equals(item.CommandName, "removed", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(retained, "invalid.prompt.md"), "Invalid source.");

        configuration.Remove(ArtifactSourceKind.Prompt, removed);

        catalogue.IsCurrent(original.Id, original.Digest).Should().BeFalse();
        reload.ReloadNow().Should().BeFalse();
        catalogue.Artifacts.Should().NotContain(item => item.CommandName == "removed");
        File.Exists(removedFile).Should().BeTrue();
    }

    [Fact]
    public async Task Missing_supported_tree_is_published_to_native_commands_and_remains_watched()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            Directory.CreateDirectory(root);
            using var native = new NativeUxFixtureSession(root);
            await native.InitializeAsync();
            var profile = Path.Combine(root, "profile");
            Directory.CreateDirectory(profile);
            var prompts = Path.Combine(profile, "Code", "User", "prompts");
            Directory.Exists(Path.Combine(profile, "Code")).Should().BeFalse();
            var preferences = new Preferences(ArtifactDirectorySettings.Empty);
            using var reload = CreateReload(native.ArtifactCatalogue, native, preferences, profile);
            native.Main.BindFileArtifactFreshness((candidate, token) => reload.IsCurrentAsync(candidate, () => true, token));
            reload.Start();
            var prompt = Path.Combine(prompts, "recovered.prompt.md");

            await ObserveChange(native.ArtifactCatalogue,
                () => native.ArtifactCatalogue.Artifacts.Any(item => string.Equals(item.CommandName, "recovered", StringComparison.Ordinal)),
                () =>
                {
                    Directory.CreateDirectory(prompts);
                    WritePrompt(prompt, "Recovered", "First supported source.");
                });
            await native.Main.ArtifactChangeUpdate;
            native.Main.CommandText = "/";
            native.Main.ArtifactCommandOptions.Should().Contain(item => item.Name == "Recovered");
            var original = native.ArtifactCatalogue.Artifacts.Single(item => string.Equals(item.CommandName, "recovered", StringComparison.Ordinal));

            await ObserveChange(native.ArtifactCatalogue,
                () => !native.ArtifactCatalogue.IsCurrent(original.Id, original.Digest),
                () => WritePrompt(prompt, "Recovered", "Changed supported source."));
            native.ArtifactCatalogue.Artifacts.Single(item => string.Equals(item.CommandName, "recovered", StringComparison.Ordinal))
                .Content.Should().Be("Changed supported source.");
            await native.Main.ArtifactChangeUpdate;
            native.Main.CommandText = "/recovered";
            await native.Main.RunTypedCommand.ExecuteAsync();
            native.Main.ResponseTitle.Should().Be("Model use is turned off.");
            native.Main.IsModelQuestionPending.Should().BeFalse();
            native.Main.IsModelActionApprovalPending.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Populated_dotted_profile_subtree_move_rename_and_delete_refresh_the_catalogue(int depth)
    {
        var profile = Path.Combine(root, "profile");
        var profiles = Path.Combine(profile, "Code", "User", "profiles");
        var parent = depth == 1 ? profiles : Path.Combine(profiles, "profile.one");
        Directory.CreateDirectory(parent);
        var outside = Path.Combine(root, "staged.tree");
        Directory.CreateDirectory(outside);
        WritePrompt(Path.Combine(outside, "moved.prompt.md"), "Moved", "Moved source.");
        var catalogue = EmbeddedArtifactCatalogue.Load();
        var paths = new Paths(Path.Combine(root, "kora"));
        var preferences = new Preferences(ArtifactDirectorySettings.Empty);
        var mcp = new McpServerConfigurationService(paths, preferences, profile);
        using var reload = CreateReload(catalogue, paths, preferences, profile, mcp: mcp);
        reload.Start();
        var inside = Path.Combine(parent, "populated.tree");
        await ObserveChange(catalogue,
            () => catalogue.Artifacts.Any(item => string.Equals(item.CommandName, "moved", StringComparison.Ordinal)),
            () => Directory.Move(outside, inside));
        var original = catalogue.Artifacts.Single(item => string.Equals(item.CommandName, "moved", StringComparison.Ordinal));
        var renamed = Path.Combine(parent, "renamed.tree");
        await ObserveChange(catalogue,
            () => !string.Equals(catalogue.Artifacts.Single(item => string.Equals(item.CommandName, "moved", StringComparison.Ordinal)).UseControlId, original.UseControlId, StringComparison.Ordinal),
            () => Directory.Move(inside, renamed), mcp);
        await ObserveChange(catalogue,
            () => !catalogue.Artifacts.Any(item => string.Equals(item.CommandName, "moved", StringComparison.Ordinal)),
            () => Directory.Move(renamed, outside));
        await ObserveChange(catalogue,
            () => catalogue.Artifacts.Any(item => string.Equals(item.CommandName, "moved", StringComparison.Ordinal)),
            () => Directory.Move(outside, inside));
        await ObserveChange(catalogue,
            () => !catalogue.Artifacts.Any(item => string.Equals(item.CommandName, "moved", StringComparison.Ordinal)),
            () => Directory.Delete(inside, recursive: true));
    }

    [Fact]
    public async Task Ancestor_subscription_ignores_outside_depth_and_unrelated_file_notifications()
    {
        var profile = Path.Combine(root, "profile");
        var profiles = Path.Combine(profile, "Code", "User", "profiles");
        var deepest = Path.Combine(profiles, "profile.one", "prompts");
        Directory.CreateDirectory(deepest);
        var logger = new ActivityCapturingLogger();
        var catalogue = EmbeddedArtifactCatalogue.Load();
        using var reload = CreateReload(catalogue, new Paths(Path.Combine(root, "kora")),
            new Preferences(ArtifactDirectorySettings.Empty), profile, logger);
        reload.Start();
        using var observed = new FileSystemWatcher(profile)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName,
        };
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observed.Deleted += (_, args) =>
        {
            if (string.Equals(args.Name, "sentinel", StringComparison.Ordinal)) { delivered.TrySetResult(); }
        };
        observed.EnableRaisingEvents = true;
        var outside = Path.Combine(profile, "unrelated.tree");
        Directory.CreateDirectory(outside);
        WritePrompt(Path.Combine(outside, "outside.prompt.md"), "Outside", "Not admitted.");
        var tooDeep = Path.Combine(deepest, "too.deep");
        Directory.CreateDirectory(tooDeep);
        WritePrompt(Path.Combine(tooDeep, "deep.prompt.md"), "Deep", "Not admitted.");
        var noise = Path.Combine(profiles, "storage.json");
        File.WriteAllText(noise, "{}");
        File.Move(noise, noise + ".old");
        File.Delete(noise + ".old");
        File.WriteAllText(Path.Combine(profiles, "extensionless"), "Not a directory.");
        File.Delete(Path.Combine(profiles, "extensionless"));
        File.WriteAllText(Path.Combine(profile, "sentinel"), string.Empty);
        File.Delete(Path.Combine(profile, "sentinel"));
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(700), TestContext.Current.CancellationToken);
        logger.ReloadActivity.Task.IsCompleted.Should().BeFalse();
        catalogue.Artifacts.Should().NotContain(item => item.CommandName == "outside" || item.CommandName == "deep");
    }

    [Fact]
    public async Task Disposal_joins_an_in_flight_reload_and_prevents_late_publication()
    {
        var prompts = Path.Combine(root, "prompts");
        Directory.CreateDirectory(prompts);
        var prompt = Path.Combine(prompts, "joined.prompt.md");
        var catalogue = EmbeddedArtifactCatalogue.Load();
        using var logger = new BlockingReloadLogger();
        var reload = CreateReload(catalogue, new Paths(Path.Combine(root, "kora")),
            new Preferences(new([], [], [prompts], [], [], [])), Path.Combine(root, "profile"), logger);
        using (reload)
        {
            reload.Start();
            WritePrompt(prompt, "Joined", "Current source.");
            await logger.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var disposal = Task.Run(() =>
            {
                started.SetResult();
                reload.Dispose();
            }, TestContext.Current.CancellationToken);
            try
            {
                await started.Task;
                await Task.WhenAny(disposal, Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
                disposal.IsCompleted.Should().BeFalse();
            }
            finally
            {
                logger.Release.Set();
                await disposal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            }
            var published = catalogue.Candidates;
            WritePrompt(prompt, "Late", "Must not publish.");
            reload.ReloadNow().Should().BeFalse();
            catalogue.Candidates.Should().BeSameAs(published);
            var start = () => reload.Start();
            start.Should().Throw<ObjectDisposedException>();
        }
    }

    private static ArtifactCatalogueReloadService CreateReload(
        ArtifactCatalogue catalogue, IApplicationDataPaths paths, Preferences preferences, string profile,
        ILogger<ArtifactCatalogueReloadService>? logger = null, McpServerConfigurationService? mcp = null) =>
        new(catalogue, paths, preferences, new ArtifactDirectoryConfigurationService(preferences, new Audit()),
            mcp ?? new McpServerConfigurationService(paths, preferences, profile),
            logger ?? NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile);

    private static async Task ObserveChange(ArtifactCatalogue catalogue, Func<bool> complete, Action change,
        McpServerConfigurationService? mcp = null)
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, EventArgs args)
        {
            if (complete()) { observed.TrySetResult(); }
        }
        void Failed(object? sender, ArtifactCatalogueReloadFailedEventArgs args) =>
            observed.TrySetException(args.Exception);
        catalogue.Changed += Changed;
        catalogue.ReloadFailed += Failed;
        if (mcp is not null) { mcp.Changed += Changed; }
        try
        {
            change();
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            catalogue.Changed -= Changed;
            catalogue.ReloadFailed -= Failed;
            if (mcp is not null) { mcp.Changed -= Changed; }
        }
    }

    [Fact]
    public async Task Disk_change_publishes_new_revision_and_invalid_change_withholds_last_valid_catalogue()
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
        catalogue.Candidates.Single(artifact =>
                string.Equals(artifact.CommandName, "updated", StringComparison.Ordinal))
            .Digest.Should().Be(updatedDigest);
        catalogue.Artifacts.Should().OnlyContain(artifact => artifact.UseControlId == null);
        catalogue.IsReloadUnavailable.Should().BeTrue();
        await ObserveChange(catalogue,
            () => catalogue.Artifacts.Any(item => string.Equals(item.CommandName, "recovered", StringComparison.Ordinal)),
            () => WritePrompt(prompt, "Recovered", "Valid again."));
        catalogue.IsReloadUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task MissedWatcherChangeCannotUseAnApprovedStaleSourceAndRestorationNeedsNewLocalReview()
    {
        var profile = Path.Combine(root, "profile");
        var prompts = Path.Combine(profile, "Code", "User", "prompts");
        Directory.CreateDirectory(prompts);
        var prompt = Path.Combine(prompts, "example.prompt.md");
        WritePrompt(prompt, "Example", "Original source.");
        var paths = new Paths(Path.Combine(root, "kora"));
        var preferences = new Preferences(ArtifactDirectorySettings.Empty);
        var use = new ArtifactUsePreferences(paths);
        using var catalogue = new ArtifactCatalogue(EmbeddedArtifactCatalogue.Load().Artifacts, use);
        using var reload = new ArtifactCatalogueReloadService(catalogue, paths, preferences,
            new ArtifactDirectoryConfigurationService(preferences, new Audit()),
            new McpServerConfigurationService(paths, preferences, profile),
            NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile, use: use);
        reload.ReloadNow().Should().BeTrue();
        var selected = catalogue.Candidates.Single(item => item.UseControlId != null);
        catalogue.IsCurrent(selected.Id, selected.Digest).Should().BeFalse();
        var saved = new OwnedInstructionSnapshot("owned", new string('a', 48), new string('b', 48),
            ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(
                "---\nkora-format: 1\nname: owned\ndescription: Test\n---\nSaved source.", false),
                ArtifactKind.Instruction, "kora-profile", "owned"));
        reload.ReconcileSavedCopy(saved).Should().BeTrue();
        use.SetDisabled(selected.UseControlId!, false, selected.Digest);
        (await reload.IsCurrentAsync(selected, () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
        (await reload.IsCurrentAsync(selected, () => false, TestContext.Current.CancellationToken)).Should().BeFalse();
        WritePrompt(prompt, "Example", "Externally changed without starting watchers.");
        catalogue.IsCurrent(selected.Id, selected.Digest).Should().BeTrue("the test deliberately bypasses watcher delivery");
        (await reload.IsCurrentAsync(selected, () => true, TestContext.Current.CancellationToken)).Should().BeFalse();
        catalogue.IsReloadUnavailable.Should().BeTrue();
        reload.ReconcileSavedCopy(saved).Should().BeFalse("an exact save cannot certify other failed sources");
        catalogue.Artifacts.Should().OnlyContain(item => item.UseControlId == null);
        use.IsRevisionAllowed(selected.UseControlId!, selected.Digest).Should().BeFalse();
        WritePrompt(prompt, "Example", "Original source.");
        reload.ReloadNow().Should().BeTrue();
        catalogue.IsCurrent(selected.Id, selected.Digest).Should().BeFalse();
        new ArtifactUsePreferences(paths).IsRevisionAllowed(selected.UseControlId!, selected.Digest).Should().BeFalse();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await reload.Invoking(owner => owner.IsCurrentAsync(selected, () => true, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        use.SetDisabled(selected.UseControlId!, false, selected.Digest);
        File.Delete(prompt);
        (await reload.IsCurrentAsync(selected, () => true, TestContext.Current.CancellationToken)).Should().BeFalse();
        reload.Dispose();
        (await reload.IsCurrentAsync(selected, () => true, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public void IncompleteOrConflictingIncomingDefinitionsHoldUseWithoutPublishingPartialOrDuplicateCommands()
    {
        var profile = Path.Combine(root, "profile");
        var prompts = Path.Combine(profile, "Code", "User", "prompts");
        Directory.CreateDirectory(prompts);
        var prompt = Path.Combine(prompts, "example.prompt.md");
        WritePrompt(prompt, "Example", "Original source.");
        var paths = new Paths(Path.Combine(root, "kora"));
        var preferences = new Preferences(ArtifactDirectorySettings.Empty);
        var use = new ArtifactUsePreferences(paths);
        using var catalogue = new ArtifactCatalogue(EmbeddedArtifactCatalogue.Load().Artifacts, use);
        using var reload = new ArtifactCatalogueReloadService(catalogue, paths, preferences,
            new ArtifactDirectoryConfigurationService(preferences, new Audit()),
            new McpServerConfigurationService(paths, preferences, profile),
            NullLogger<ArtifactCatalogueReloadService>.Instance, profileRoamingRoot: profile, use: use);
        reload.ReloadNow().Should().BeTrue();
        var selected = catalogue.Candidates.Single(item => item.UseControlId != null);
        use.SetDisabled(selected.UseControlId!, false, selected.Digest);
        File.WriteAllText(prompt, "---\nname: Example\n");
        reload.ReloadNow().Should().BeFalse();
        catalogue.Artifacts.Should().OnlyContain(item => item.UseControlId == null);
        File.ReadAllText(prompt).Should().Be("---\nname: Example\n");
        WritePrompt(prompt, "Example", "Original source.");
        reload.ReloadNow().Should().BeTrue();
        catalogue.IsCurrent(selected.Id, selected.Digest).Should().BeFalse();
        use.SetDisabled(selected.UseControlId!, false, selected.Digest);
        WritePrompt(Path.Combine(prompts, "conflict.prompt.md"), "Example", "A competing copy.");
        reload.ReloadNow().Should().BeFalse();
        catalogue.IsReloadUnavailable.Should().BeTrue();
        catalogue.Artifacts.Should().OnlyContain(item => item.UseControlId == null);
        File.ReadAllText(prompt).Should().Contain("Original source.");
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

    private sealed class BlockingReloadLogger : ILogger<ArtifactCatalogueReloadService>, IDisposable
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != 2600) { return; }
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) { throw new TimeoutException("Reload was not released."); }
        }
        public void Dispose() => Release.Dispose();
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
