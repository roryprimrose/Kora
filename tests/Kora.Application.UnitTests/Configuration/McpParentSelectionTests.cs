using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Mcp;

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