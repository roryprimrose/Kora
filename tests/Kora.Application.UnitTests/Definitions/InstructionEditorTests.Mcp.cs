using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Customizations;

namespace Kora.Application.UnitTests.Definitions;

public sealed partial class InstructionEditorTests
{
    [Theory]
    [InlineData("save")]
    [InlineData("cancel")]
    [InlineData("exclusion-failure")]
    [InlineData("source-failure")]
    [InlineData("missing-control")]
    [InlineData("user-source")]
    public async Task McpSaveIndependentlyExcludesBeforePublicationAndNeverRevivesAuthority(string state)
    {
        await using var f = new Fixture();
        var files = new CustomizationFiles(CustomizationKind.McpServer,
            state is "user-source" ? CustomizationScope.User : CustomizationScope.Kora);
        var use = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store());
        var id = ArtifactUseIdentity.McpServerId(files.Source.Location, files.Source.Name);
        var preferences = new CustomizationMcpPreferences { Settings = new([], [], [], [], [], [id]) };
        var configuration = state is "missing-control" ? null : new ArtifactDirectoryConfigurationService(preferences, f.Audit);
        var service = new CustomizationManagementService(files, f.Admission, use, f.Catalogue, f.Audit, configuration);
        if (state is "exclusion-failure") { preferences.Failure = new IOException("Synthetic preference failure"); }
        files.FailWrite = state is "source-failure";
        files.AfterWrite = () =>
        {
            use.IsDisabled(files.Source.Id).Should().BeTrue();
            preferences.Settings.IncludedMcpServerIds.Should().BeEmpty();
        };
        Func<Task<CustomizationSnapshot?>> save = () => service.Save(files.Source, files.Current, files.Current.Files,
            _ => Task.FromResult(state is not "cancel"), f.Eligible, TestContext.Current.CancellationToken);
        if (state is "save") { (await save()).Should().NotBeNull(); }
        else if (state is "cancel") { (await save()).Should().BeNull(); }
        else { await save.Should().ThrowAsync<Exception>(); }
        files.Writes.Should().Be(state is "save" ? 1 : 0);
        foreach (var entry in f.Audit.Events)
        {
            string.Join(" ", entry.ActionId, entry.TargetId, entry.ReasonCode).Should()
                .NotContain("https:").And.NotContain("example.invalid");
            entry.ApprovalId.Should().BeNull();
        }
        if (state is "cancel" or "missing-control" or "user-source")
        {
            f.Audit.Events.Should().BeEmpty();
            use.IsDisabled(files.Source.Id).Should().BeFalse();
            preferences.Settings.IncludedMcpServerIds.Should().Equal(id);
        }
        else
        {
            use.IsDisabled(files.Source.Id).Should().BeTrue();
            if (state is not "exclusion-failure") { preferences.Settings.IncludedMcpServerIds.Should().BeEmpty(); }
            if (state is not "save")
            { f.Audit.Events.Should().NotContain(entry => entry.ActionId == "definition.source.save" && entry.Outcome == SecurityAuditOutcome.Succeeded); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void McpSourceAndVoiceAuthoringNeverReachAModel(bool voice)
    {
        var service = new CustomizationAuthoringService(null!, null!, TimeProvider.System);
        Action attempt = () =>
        {
            if (voice)
            {
                _ = service.Interpret(new(CustomizationKind.McpServer, CustomizationScope.Kora, "example", null, false, false),
                    "edit", () => true, TestContext.Current.CancellationToken);
            }
            else
            {
                var source = new CustomizationFiles(CustomizationKind.McpServer);
                _ = service.Refine(source.Source, source.Current.Files, new(null), "edit", () => true, TestContext.Current.CancellationToken);
            }
        };
        attempt.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("native");
    }

    private sealed class CustomizationMcpPreferences : IArtifactDirectoryPreferences
    {
        internal ArtifactDirectorySettings Settings = ArtifactDirectorySettings.Empty;
        internal Exception? Failure;
        public ArtifactDirectorySettings Load() => Settings;
        public void Save(ArtifactDirectorySettings settings)
        {
            if (Failure is not null) { throw Failure; }
            Settings = settings;
        }
    }
}
