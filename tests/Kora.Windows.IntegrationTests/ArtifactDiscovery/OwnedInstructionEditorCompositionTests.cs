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
using Kora.Core.Commands;
using Kora.Windows.Artifacts;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.ArtifactDiscovery;

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
    [InlineData(RequestOrigin.LocalUi, ArtifactKind.Instruction)]
    [InlineData(RequestOrigin.ActivatedVoice, ArtifactKind.Instruction)]
    [InlineData(RequestOrigin.LocalUi, ArtifactKind.Prompt)]
    [InlineData(RequestOrigin.ActivatedVoice, ArtifactKind.Prompt)]
    public async Task NativeReviewedSaveUsesActualDurableSessionWriterReadbackAndDisabledSharedCatalogue(RequestOrigin origin, ArtifactKind kind)
    {
        var paths = new Paths(root);
        Directory.CreateDirectory(paths.RoamingRoot);
        var tasks = new WindowsSqliteHostTaskStore(paths);
        await tasks.InitializeAsync(TestContext.Current.CancellationToken);
        var interactions = new WindowsSqliteHostInteractionStore(paths, tasks, new Clock());
        await interactions.InitializeAsync(TestContext.Current.CancellationToken);
        await using var admission = new InstructionControlAdmission(interactions, interactions, new HostTaskCoordinator(tasks));
        var store = new WindowsOwnedInstructionStore(paths, kind);
        var revisions = new InstructionRevisionCatalogue(paths, kind);
        var preferences = new Preferences();
        var audit = new Audit();
        var bundled = new ArtifactDefinition("bundled.test", ArtifactKind.Prompt, "Bundled", "Read only", "test",
            ["test"], "bundled", "1", new string('a', 64), "Body");
        var catalogue = new ArtifactCatalogue(revisions.Reconcile([bundled]));
        var actions = new OwnedInstructionEditorActions(store, admission, revisions, catalogue, audit,
            new BuiltInCommandRouter(new BuiltInCommandCatalog()), kind);
        var workflow = new InstructionSaveWorkflow(actions);
        using var reload = new ArtifactCatalogueReloadService(catalogue, paths, preferences,
            new ArtifactDirectoryConfigurationService(preferences, audit),
            new McpServerConfigurationService(paths, preferences, Path.Combine(root, "no-profile")),
            NullLogger<ArtifactCatalogueReloadService>.Instance,
            kind == ArtifactKind.Instruction ? revisions : new InstructionRevisionCatalogue(paths),
            kind == ArtifactKind.Instruction ? store : new WindowsOwnedInstructionStore(paths), Path.Combine(root, "no-profile"),
            kind == ArtifactKind.Prompt ? revisions : new InstructionRevisionCatalogue(paths, ArtifactKind.Prompt),
            kind == ArtifactKind.Prompt ? store : new WindowsOwnedInstructionStore(paths, ArtifactKind.Prompt));
        var text = "---\r\nkora-format: 1\r\nname: example\r\ndescription: Test\r\n---\r\nNative draft.\r\n";
        using var originalInput = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
        var review = new Review(text);
        var saved = await workflow.Save("example", null, text, false, review, () => true, TestContext.Current.CancellationToken);
        saved.Saved.Should().BeTrue();
        review.Reviews.Should().Be(1);
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(item => string.Equals(item.TargetId,
            kind == ArtifactKind.Instruction ? "definition.owned-instruction" : "definition.owned-prompt", StringComparison.Ordinal));
        reload.ReloadNow().Should().BeTrue();
        catalogue.Candidates.Should().Contain(item => string.Equals(item.OwnedInstructionName, "example", StringComparison.Ordinal) && !item.IsUseEnabled);
        var router = new ArtifactCommandRouter(catalogue);
        var command = "/" + kind.ToString().ToLowerInvariant() + " example";
        router.Match(command, "Kora").Invocation.Should().BeNull();
        (await actions.Activate(saved.Current!, _ => Task.FromResult(true), () => true, TestContext.Current.CancellationToken))
            .Should().BeTrue();
        router.Match(command, "Kora").Invocation!.Artifact.Digest.Should().Be(saved.Current!.Document.Definition.Digest);
        (await actions.IsCurrent(router.Match(command, "Kora").Invocation!.Artifact, () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
        var originalBytes = saved.Current!.Document.CopyBytes();
        var draft = text + "Unsaved changes retained in a separate draft.";
        var copyReview = new CopyReview(saved.Current, draft);
        var copied = await workflow.SaveCopy("separate", saved.Current, draft, copyReview, () => true,
            TestContext.Current.CancellationToken, reload.ReconcileSavedCopy);
        copyReview.Reviews.Should().Be(1);
        copied.Published.Should().BeTrue();
        copied.Current!.FileIdentity.Should().NotBe(saved.Current.FileIdentity);
        copied.Current.DirectoryIdentity.Should().Be(saved.Current.DirectoryIdentity);
        router.Match(command, "Kora").Invocation.Should().NotBeNull();
        router.Match("/" + kind.ToString().ToLowerInvariant() + " separate", "Kora").Invocation.Should().BeNull();
        File.ReadAllBytes(Path.Combine(paths.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind), "example" + OwnedDefinitionKind.Extension(kind)))
            .Should().Equal(originalBytes);
        audit.Events.Last().ActionId.Should().Be("definition." + kind.ToString().ToLowerInvariant() + ".copy");
        audit.Events.Last().TargetId.Should().StartWith("definition.owned-copy.");
        reload.ReloadNow().Should().BeTrue();
        router.Match(command, "Kora").Invocation.Should().NotBeNull();
        (await actions.Activate(copied.Current, _ => Task.FromResult(true), () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
        var disabled = await actions.Disable(saved.Current, _ => Task.FromResult(true), () => true, TestContext.Current.CancellationToken);
        disabled.WasEnabled.Should().BeTrue();
        router.Match(command, "Kora").Invocation.Should().BeNull();
        router.Match("/" + kind.ToString().ToLowerInvariant() + " separate", "Kora").Invocation.Should().NotBeNull();
        catalogue.Candidates.Should().Contain(item => item.Kind == kind && item.OwnedInstructionName == "example" && !item.IsUseEnabled);
        reload.ReloadNow().Should().BeTrue();
        router.Match(command, "Kora").Invocation.Should().BeNull();
        router.Match("/" + kind.ToString().ToLowerInvariant() + " separate", "Kora").Invocation.Should().NotBeNull();
        new InstructionRevisionCatalogue(paths, kind).Reconcile(catalogue.Candidates)
            .Where(item => item.Kind == kind && item.OwnedInstructionName != null).Should().OnlyContain(item => !item.IsUseEnabled);
        (await store.Read("example", TestContext.Current.CancellationToken))!.Document.CopyBytes().Should().Equal(originalBytes);
        actions.SuspendReviewedUse();
        router.Match(command, "Kora").Invocation.Should().BeNull();
        (await actions.Activate(saved.Current!, _ => Task.FromResult(true), () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
        var target = Path.Combine(paths.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind), "example" + OwnedDefinitionKind.Extension(kind));
        File.WriteAllText(target, text + "External edit.");
        reload.ReloadNow().Should().BeTrue();
        router.Match(command, "Kora").Invocation.Should().BeNull();
        File.WriteAllText(target, "invalid");
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
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class Audit : ISecurityAuditLog
    {
        internal readonly List<SecurityAuditEvent> Events = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
    private sealed class Review(string expected) : IInstructionMutationReview
    {
        public Task<bool> ConfirmCopy(OwnedDefinitionCopy copy, CancellationToken token) => throw new NotSupportedException();
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

    private sealed class CopyReview(OwnedInstructionSnapshot source, string draft) : IInstructionMutationReview
    {
        internal int Reviews;
        public Task<bool> ConfirmCopy(OwnedDefinitionCopy copy, CancellationToken token)
        {
            copy.Source.Should().BeSameAs(source);
            copy.Draft.Text.Should().Be(draft);
            copy.Proposed.Text.Should().Be(draft.Replace("name: example", "name: separate", StringComparison.Ordinal));
            Reviews++;
            return Task.FromResult(true);
        }
        public Task<bool> Confirm(string name, OwnedInstructionSnapshot? expected, ArtifactFileDocument? proposed, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<InstructionConflictChoice> Conflict(OwnedInstructionSnapshot? current, ArtifactFileDocument? proposed, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
