using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Mcp;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Configuration;

public sealed class McpParentSelectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavedParentSurvivesRestartIndependentlyOfHistoricalUseVeto(bool builtIn)
    {
        var store = new Store();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var settings = new ArtifactDirectoryConfigurationService(directories, new Audit());
        var use = new ArtifactUsePreferences(store);
        var controls = new McpCapabilityPreferences(use, directories, settings);
        var server = builtIn ? KoraMcpCatalog.ServerId : new string('a', 64);
        controls.SetServerEnabled(server, true);
        controls.SetToolEnabled(server, "search", false);
        use.SetDisabled(ArtifactUseIdentity.Mcp(server), true);
        controls.IsServerSelected(server).Should().BeTrue();
        controls.IsServerEnabled(server).Should().BeFalse();
        controls.SetServerEnabled(server, false);
        new McpCapabilityPreferences(new ArtifactUsePreferences(store), new LocalArtifactDirectoryPreferences(store), settings)
            .IsServerSelected(server).Should().BeFalse();
        controls.IsToolSelected(server, "search").Should().BeFalse();
        controls.SetServerEnabled(server, true);
        var restarted = new McpCapabilityPreferences(new ArtifactUsePreferences(store),
            new LocalArtifactDirectoryPreferences(store), settings);
        restarted.IsServerSelected(server).Should().BeTrue();
        restarted.IsServerEnabled(server).Should().BeFalse();
        restarted.IsToolSelected(server, "search").Should().BeFalse();
        use.IsDisabled(ArtifactUseIdentity.Mcp(server)).Should().BeTrue();
    }

    [Fact]
    public void SavedParentUsesCanonicalBuiltInInitialDefaultAndRejectsMalformedState()
    {
        var store = new Store();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var settings = new ArtifactDirectoryConfigurationService(directories, new Audit());
        var controls = new McpCapabilityPreferences(new ArtifactUsePreferences(store), directories, settings);
        controls.IsServerSelected(KoraMcpCatalog.ServerId).Should().BeTrue();
        controls.IsServerSelected(new string('a', 64)).Should().BeFalse();
        FluentActions.Invoking(() => controls.IsServerSelected("untrusted.server")).Should().Throw<ArgumentException>();
        store.Values["artifact-directories.json"] = "{}";
        FluentActions.Invoking(() => controls.IsServerSelected(KoraMcpCatalog.ServerId)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MetadataProducerProjectsCanonicalSavedParentWithAndWithoutTheHostAdapter()
    {
        var store = new Store();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var settings = new ArtifactDirectoryConfigurationService(directories, new Audit());
        var use = new ArtifactUsePreferences(store);
        var controls = new McpCapabilityPreferences(use, directories, settings);
        var configured = new McpServerConfigurationService(new Paths(), directories, capabilities: controls);
        var fallback = new McpServerConfigurationService(new Paths(), directories);
        configured.IsServerSelected(KoraMcpCatalog.ServerId).Should().BeTrue();
        fallback.IsServerSelected(KoraMcpCatalog.ServerId).Should().BeTrue();
        var external = new string('a', 64);
        fallback.IsServerSelected(external).Should().BeFalse();
        settings.SetMcpServerIncluded(external, true);
        use.SetDisabled(ArtifactUseIdentity.Mcp(external), true);
        configured.IsServerSelected(external).Should().BeTrue();
        fallback.IsServerSelected(external).Should().BeTrue();
        FluentActions.Invoking(() => fallback.IsServerSelected("untrusted.server")).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OrdinarySubmitUsesNoToolsOnlyWhenEveryEffectiveToolIsDisabled(bool parent, bool keepOne)
    {
        var store = new Store();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var settings = new ArtifactDirectoryConfigurationService(directories, new Audit());
        var controls = new McpCapabilityPreferences(new ArtifactUsePreferences(store), directories, settings);
        using var configuration = new McpServerConfigurationService(new Paths(), directories, capabilities: controls);
        var now = DateTimeOffset.UtcNow;
        var context = new ModelContextEnvelope(HostRequest.Create(RequestOrigin.LocalUi), "policy", "question", [],
            now, now.AddSeconds(15), ModelToolMode.InteractiveConversation);
        if (parent) { controls.SetServerEnabled(KoraMcpCatalog.ServerId, false); }
        else
        {
            foreach (var descriptor in context.Tools)
            {
                controls.SetToolEnabled(KoraMcpCatalog.ServerId, descriptor.Id,
                    keepOne && string.Equals(descriptor.Id, ReadOnlyCapabilityCatalog.Version, StringComparison.Ordinal));
            }
        }
        var restricted = configuration.RestrictTools(context);
        restricted.Request.Should().BeSameAs(context.Request);
        restricted.UserRequest.Should().Be(context.UserRequest);
        restricted.CreatedAt.Should().Be(context.CreatedAt);
        restricted.ExpiresAt.Should().Be(context.ExpiresAt);
        restricted.ToolMode.Should().Be(keepOne ? ModelToolMode.InteractiveConversation : ModelToolMode.None);
        restricted.Tools.Select(tool => tool.Id).Should().BeEquivalentTo(
            keepOne ? new[] { ReadOnlyCapabilityCatalog.Version } : []);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), context.Request, ModelProviderSelection.OllamaCandidate);
        var parsed = ModelToolProtocol.ReadContext(restricted.Serialize(), provenance);
        ModelToolProtocol.CreateRequest(provenance, null, parsed, [], 1).Context.ToolMode.Should().Be(restricted.ToolMode);
    }

    [Fact]
    public void NoToolsOrdinarySubmitPreservesTheCapturedInputOutputAndDeadlineLimits()
    {
        var store = new Store();
        var directories = new LocalArtifactDirectoryPreferences(store);
        var settings = new ArtifactDirectoryConfigurationService(directories, new Audit());
        var controls = new McpCapabilityPreferences(new ArtifactUsePreferences(store), directories, settings);
        controls.SetServerEnabled(KoraMcpCatalog.ServerId, false);
        using var configuration = new McpServerConfigurationService(new Paths(), directories, capabilities: controls);
        var now = DateTimeOffset.UtcNow;
        var limits = new ModelTurnConstraints(2, [ReadOnlyCapabilityCatalog.Version], 1, 2, 2048, 1024, 2000);
        var context = new ModelContextEnvelope(HostRequest.Create(RequestOrigin.LocalUi), "policy", "question", [],
            now, now.AddSeconds(2), ModelToolMode.InteractiveConversation, limits);
        var restricted = configuration.RestrictTools(context);
        restricted.Constraints!.InputUtf8Bytes.Should().Be(2048);
        restricted.Constraints.OutputUtf8Bytes.Should().Be(1024);
        restricted.Constraints.DeadlineMilliseconds.Should().Be(2000);
        restricted.Constraints.MaximumCalls.Should().Be(0);
        restricted.Constraints.MaximumRounds.Should().Be(1);
        restricted.RequireCompleteInputLimit();
    }

    private sealed class Paths : IApplicationDataPaths
    {
        public string LocalRoot => Path.GetFullPath("mcp-parent-tests");
        public string RoamingRoot => LocalRoot;
    }

    private sealed class Store : IPreferenceStore
    {
        internal Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public string? ReadText(string fileName) => Values.GetValueOrDefault(fileName);
        public void WriteText(string fileName, string contents) => Values[fileName] = contents;
        public string[]? ReadLines(string fileName) => throw new NotSupportedException();
        public void WriteLines(string fileName, IEnumerable<string> contents) => throw new NotSupportedException();
        public void Delete(string fileName) => Values.Remove(fileName);
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}