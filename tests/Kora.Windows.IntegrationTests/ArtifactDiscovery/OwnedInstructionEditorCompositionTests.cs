using System.Diagnostics;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Artifacts;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class OwnedInstructionEditorCompositionTests : IDisposable
{
    private readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        ".net-test-artifacts", "instruction-composition", Guid.NewGuid().ToString("N")));
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public OwnedInstructionEditorCompositionTests()
    {
        ActivitySource.AddActivityListener(listener);
        using var identity = WindowsIdentity.GetCurrent();
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        RestrictedStorageDirectory.CreateRestrictedDirectory(root, identity.User!);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task NativeReviewedSaveUsesActualDurableSessionWriterReadbackAndDisabledSharedCatalogue(RequestOrigin origin)
    {
        var paths = new Paths(root);
        Directory.CreateDirectory(paths.RoamingRoot);
        var tasks = new WindowsSqliteHostTaskStore(paths);
        await tasks.InitializeAsync(TestContext.Current.CancellationToken);
        var interactions = new WindowsSqliteHostInteractionStore(paths, tasks);
        await interactions.InitializeAsync(TestContext.Current.CancellationToken);
        await using var admission = new InstructionControlAdmission(interactions, interactions, new HostTaskCoordinator(tasks));
        var store = new WindowsOwnedInstructionStore(paths);
        var revisions = new InstructionRevisionCatalogue(paths);
        var preferences = new Preferences();
        var audit = new Audit();
        var bundled = new ArtifactDefinition("bundled.test", ArtifactKind.Prompt, "Bundled", "Read only", "test",
            ["test"], "bundled", "1", new string('a', 64), "Body");
        var catalogue = new ArtifactCatalogue(revisions.Reconcile([bundled]));
        var actions = new OwnedInstructionEditorActions(store, admission, revisions, catalogue, audit);
        var workflow = new InstructionSaveWorkflow(actions);
        using var reload = new ArtifactCatalogueReloadService(catalogue, paths, preferences,
            new ArtifactDirectoryConfigurationService(preferences, audit),
            new McpServerConfigurationService(paths, preferences, Path.Combine(root, "no-profile")),
            NullLogger<ArtifactCatalogueReloadService>.Instance, revisions, store, Path.Combine(root, "no-profile"));
        var text = "---\r\nkora-format: 1\r\nname: example\r\ndescription: Test\r\n---\r\nNative draft.\r\n";
        using var originalInput = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var review = new Review(text);
        var saved = await workflow.Save("example", null, text, false, review, () => true, TestContext.Current.CancellationToken);
        saved.Saved.Should().BeTrue();
        review.Reviews.Should().Be(1);
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(item => string.Equals(item.TargetId, "definition.owned-instruction", StringComparison.Ordinal));
        reload.ReloadNow().Should().BeTrue();
        catalogue.Candidates.Should().Contain(item => string.Equals(item.OwnedInstructionName, "example", StringComparison.Ordinal) && !item.IsUseEnabled);
        var router = new ArtifactCommandRouter(catalogue);
        router.Match("/instruction example", "Kora").Invocation.Should().BeNull();
        (await actions.Activate(saved.Current!, _ => Task.FromResult(true), () => true, TestContext.Current.CancellationToken))
            .Should().BeTrue();
        router.Match("/instruction example", "Kora").Invocation!.Artifact.Digest.Should().Be(saved.Current!.Document.Definition.Digest);
        File.WriteAllText(Path.Combine(paths.RoamingRoot, "Instructions", "example.instructions.md"), text + "External edit.");
        reload.ReloadNow().Should().BeTrue();
        router.Match("/instruction example", "Kora").Invocation.Should().BeNull();
        File.WriteAllText(Path.Combine(paths.RoamingRoot, "Instructions", "example.instructions.md"), "invalid");
        reload.ReloadNow().Should().BeFalse();
        catalogue.Artifacts.Should().NotContain(item => item.OwnedInstructionName != null);
    }

    public void Dispose()
    {
        listener.Dispose();
        if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
    }

    private sealed record Paths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => Path.Combine(LocalRoot, "Roaming");
    }
    private sealed class Preferences : IArtifactDirectoryPreferences
    {
        public ArtifactDirectorySettings Load() => ArtifactDirectorySettings.Empty;
        public void Save(ArtifactDirectorySettings settings) => throw new NotSupportedException();
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal readonly List<SecurityAuditEvent> Events = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
    private sealed class Review(string expected) : IInstructionMutationReview
    {
        internal int Reviews;
        public Task<bool> Confirm(string name, OwnedInstructionSnapshot? original, ArtifactFileDocument? proposed, CancellationToken token)
        {
            proposed!.Text.Should().Be(expected);
            original.Should().BeNull();
            Reviews++;
            return Task.FromResult(true);
        }
        public Task<InstructionConflictChoice> Conflict(OwnedInstructionSnapshot? current, ArtifactFileDocument? proposed, CancellationToken token) =>
            throw new InvalidOperationException("No conflict expected.");
    }
}
