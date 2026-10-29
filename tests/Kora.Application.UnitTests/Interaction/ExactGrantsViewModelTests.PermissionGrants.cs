using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Customizations;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network;

namespace Kora.Application.UnitTests.Interaction;

public sealed partial class ExactGrantsViewModelTests
{
    [Fact]
    public async Task LegacyInventoryConstructionHasNoImplicitUriManagementAndKeepsExplicitHistoryMode()
    {
        await using var f = new Fixture();
        using var model = new ExactGrantsViewModel(f.Store, f.Control, f.Access, () => true,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ExactGrantsViewModel>.Instance);
        model.PermissionContext.Should().Be("Retained grant inventory");
        model.CanFilterStoredStatus.Should().BeTrue();
        model.ShowRetainedHistory = true;
        model.ShowRetainedHistory = false;
        model.StatusFilter.Should().Be("Any");
        model.CanChangeUriRules.Should().BeFalse();
        await model.RefreshAsync(f.Token);
        model.Records.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExactInspectionDisplaysRecordedTargetWithoutInventingMissingOwnership(bool knownScope)
    {
        await using var f = new Fixture();
        var typed = Target(f.Preview, PermissionGrantArtifactKind.Tool, knownScope ? CustomizationScope.User : null);
        f.Store.Current = typed;
        await f.State.RefreshAsync(f.Token);
        f.State.Selected = f.State.Records.Single();
        await f.State.InspectAsync(f.Token);
        f.State.Inspection.Should().Contain("Target artefact type: Tool")
            .And.Contain("artefact scope: " + (knownScope ? "User" : "Unknown"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagementViewsDefaultToActiveAndRequireExplicitScopedHistory(bool sessionView)
    {
        HostId<SessionIdentity>? session = sessionView ? new(Guid.NewGuid()) : null;
        await using var f = new Fixture(managementView: true, managementSession: session);
        f.State.PermissionContext.Should().Contain(sessionView ? session!.Value.Value.ToString("D") : "Always grants and device-wide");
        f.State.CanFilterStoredStatus.Should().BeFalse();
        await f.State.RefreshAsync(f.Token);
        f.Store.LastQuery.Should().Be(new ExactGrantQuery(status: OperationGrantStatus.Active,
            managementView: true, managementSession: session));
        f.State.SelectedPermission = f.State.PermissionRows[0];
        f.State.ShowRetainedHistory = true;
        f.State.ShowRetainedHistory = true;
        f.State.PermissionRows.Should().BeEmpty();
        f.State.SelectedPermission.Should().BeNull();
        f.State.CanFilterStoredStatus.Should().BeTrue();
        await f.State.RefreshAsync(f.Token);
        f.Store.LastQuery.Should().Be(new ExactGrantQuery(managementView: true, managementSession: session,
            includeOnceHistory: true));
        f.State.StatusFilter = "Revoked";
        f.State.ClearFilters();
        f.State.StatusFilter.Should().Be("Any");
        f.State.ShowRetainedHistory = false;
        f.State.ClearFilters();
        f.State.StatusFilter.Should().Be("Active");
        await f.State.RefreshAsync(f.Token);
        f.Store.LastQuery!.Status.Should().Be(OperationGrantStatus.Active);
    }

    [Fact]
    public async Task SharedInventoryGroupsRecordedTargetMetadataWithoutReadsWritesOrGlobalReordering()
    {
        var preferences = new UriPreferences { Settings = PreapprovedUriSettings.Create(["https://example.com/*"]) };
        var audit = new UriAudit();
        await using var f = new Fixture(uris: new PreapprovedUriConfigurationService(preferences, audit));
        var builtin = Target(f.Preview, PermissionGrantArtifactKind.Tool, CustomizationScope.BuiltIn);
        var secondBuiltin = Target(f.Preview, PermissionGrantArtifactKind.Tool, CustomizationScope.BuiltIn);
        var skill = Target(f.Preview, PermissionGrantArtifactKind.Skill, CustomizationScope.Kora);
        var prompt = Target(f.Preview, PermissionGrantArtifactKind.Prompt, CustomizationScope.User);
        f.Store.Additional = [prompt, builtin, skill, secondBuiltin];
        f.Store.Next = new("store", 1, new string('a', 64), Guid.NewGuid());
        await f.State.RefreshAsync(f.Token);
        var source = f.State.Records;
        var reads = f.Store.Pages;
        f.State.Grouping.Should().Be("None");
        f.State.PermissionRows.Should().HaveCount(6).And.OnlyContain(row => !row.HasGroupHeading);
        f.State.PermissionRows[0].Grant.Target.Should().BeNull();
        f.State.PermissionRows[0].Grant.ArtifactType.Should().Be("Unknown");
        f.State.Grouping = "Artefact type";
        f.State.PermissionRows.Where(row => row.HasGroupHeading).Select(row => row.GroupHeading)
            .Should().Equal("Prompt (1)", "Skill (1)", "Tool (2)", "URI pattern (1)", "Unknown (1)");
        f.State.Grouping = "Artefact scope";
        f.State.PermissionRows.Where(row => row.HasGroupHeading).Select(row => row.GroupHeading)
            .Should().Equal("Built-in (2)", "Kora (1)", "Unknown (2)", "User (1)");
        f.State.Grouping = "Artefact scope";
        f.State.Grouping = "None";
        f.State.PermissionRows.OfType<PermissionGrantRow>().Select(row => row.Grant.Name)
            .Should().Equal(source.Select(item => item.Grant.Id.Value.ToString("D")).Append("https://example.com/*"));
        f.State.Records.Should().Equal(source);
        f.Store.Pages.Should().Be(reads);
        f.Store.Revokes.Should().Be(0);
        preferences.Saves.Should().Be(0);
        audit.Events.Should().BeEmpty();
        await f.State.NextAsync(f.Token);
        f.Store.LastCursor.Should().BeSameAs(f.Store.Next);
        f.State.Dispose();
        f.State.CanGroup.Should().BeFalse();
        f.State.PermissionRows.Should().BeEmpty();
        f.State.Invoking(state => state.Grouping = "Artefact type").Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task RegroupingRetiresSingleAndBatchAuthorityAndRejectsForeignRows()
    {
        await using var f = new Fixture();
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.State.Grouping = "Artefact type";
        f.State.Selected.Should().BeNull();
        f.State.SelectedPermission.Should().BeNull();
        f.State.Reviewed.Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        var row = f.State.PermissionRows.Single();
        f.State.UriInspection.Should().Contain("Select a URI rule");
        f.State.SelectedPermission = row;
        f.State.SelectedPermission = row;
        f.State.SelectedPermission = null;
        f.State.SelectedPermission = row;
        f.State.SetPermissionSelection([row]);
        await f.State.PreviewBatchAsync(f.Token);
        f.State.BatchReviewed = true;
        f.State.Grouping = "Artefact scope";
        f.State.CanRevokeBatch.Should().BeFalse();
        f.State.BatchReviewed.Should().BeFalse();
        f.State.HistoryRows.Should().BeEmpty();
        f.Store.Revokes.Should().Be(0);
        f.State.Invoking(state => state.SelectedPermission = row).Should().Throw<InvalidOperationException>();
        f.State.Invoking(state => state.SetPermissionSelection([row])).Should().Throw<InvalidOperationException>();
        var current = f.State.PermissionRows.Single();
        f.State.Invoking(state => state.SetPermissionSelection([current, current])).Should().Throw<InvalidOperationException>();
        f.State.Invoking(state => state.Grouping = "unsupported").Should().Throw<ArgumentException>();
        f.State.ReportFailure(new IOException("private"));
        f.State.PermissionRows.Should().BeEmpty();
    }

    [Fact]
    public async Task BusyGroupingCannotRetargetAnInFlightInventoryAndEmptyPagesRemainGroupable()
    {
        await using var f = new Fixture();
        var pending = new TaskCompletionSource<ExactGrantPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingPage = pending.Task;
        var read = f.State.RefreshAsync(f.Token);
        f.State.CanGroup.Should().BeFalse();
        f.State.Invoking(state => state.Grouping = "Artefact type").Should().Throw<InvalidOperationException>();
        pending.SetResult(new([], null));
        await read;
        f.State.Grouping = "Artefact type";
        f.State.PermissionRows.Should().BeEmpty();
        f.State.Grouping = "None";
    }

    [Fact]
    public async Task UriRulesUseSharedInventoryAndExistingAuditedPolicyWithoutJoiningExactBatches()
    {
        var preferences = new UriPreferences();
        var audit = new UriAudit();
        var policy = new PreapprovedUriConfigurationService(preferences, audit);
        await using var f = new Fixture(uris: policy);
        await f.InspectAsync();
        f.State.Reviewed = true;
        f.State.UriPatternInput = "HTTPS://EXAMPLE.COM:443/*";
        await f.State.AddUriRuleCommand.ExecuteAsync();
        policy.IsPreapproved(new("https://example.com/page")).Should().BeTrue();
        f.State.Reviewed.Should().BeFalse();
        f.State.UriPatternInput.Should().BeEmpty();
        f.State.Message.Should().Contain("saved and read back");
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.LocalUser
            && string.Equals(item.ActionId, "configuration.preapproved-uri", StringComparison.Ordinal));
        var uriRow = f.State.PermissionRows.Single(row => row.Grant is UriPermissionGrant);
        f.State.SelectedPermission = uriRow;
        f.State.SetPermissionSelection([uriRow]);
        f.State.UriInspection.Should().Contain("https://example.com/*").And.Contain("Unknown");
        f.State.CanRemoveUriRule.Should().BeTrue();
        f.State.InspectCommand.CanExecute(null).Should().BeFalse();
        f.State.PreviewBatchCommand.CanExecute(null).Should().BeFalse();
        f.State.CanRevoke.Should().BeFalse();
        var exact = f.State.PermissionRows.Single(row => row.Grant is ExactOperationPermissionGrant);
        f.State.SetPermissionSelection([uriRow, exact]);
        f.State.CanRemoveUriRule.Should().BeFalse();
        f.State.PreviewBatchCommand.CanExecute(null).Should().BeFalse();
        f.State.SetPermissionSelection([uriRow]);
        await f.State.RemoveUriRuleCommand.ExecuteAsync();
        policy.IsPreapproved(new("https://example.com/page")).Should().BeFalse();
        f.State.PermissionRows.Should().ContainSingle();
        f.Store.Revokes.Should().Be(0);
        preferences.Saves.Should().Be(2);
        f.State.Dispose();
        f.State.PermissionRows.Should().BeEmpty();
        f.State.UriPatternInput.Should().BeEmpty();
        f.State.CanChangeUriRules.Should().BeFalse();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("control")]
    [InlineData("original-uri-admission")]
    [InlineData("missing-policy")]
    public async Task UriManagementPreservesExistingHostAdmissionAndNeverMutatesExactGrants(string change)
    {
        var preferences = new UriPreferences();
        var policy = new PreapprovedUriConfigurationService(preferences, new UriAudit());
        await using var f = new Fixture(uris: change is "missing-policy" ? null : policy);
        f.State.UriPatternInput = "https://example.com/*";
        if (change is "owner") { f.Eligible = false; }
        if (change is "control") { f.Access.CanControl = false; }
        if (change is "original-uri-admission") { f.UriEligible = false; }
        f.State.SynchronizeAuthority();
        f.State.CanChangeUriRules.Should().BeFalse();
        await f.State.AddUriRuleCommand.ExecuteAsync();
        preferences.Saves.Should().Be(0);
        f.Store.Revokes.Should().Be(0);
    }

    [Fact]
    public async Task SharedUriManagementRejectsAmbientAuthorityChangedAdmissionAndInvalidPatterns()
    {
        var preferences = new UriPreferences();
        var audit = new UriAudit();
        await using var f = new Fixture();
        var service = new PermissionGrantManagementService(f.Store, new PreapprovedUriConfigurationService(preferences, audit));
        var calls = 0;
        service.Invoking(item => item.AddUriRule("https://example.com/*", () => ++calls == 1))
            .Should().Throw<InvalidOperationException>();
        service.Invoking(item => item.AddUriRule("not-an-address", () => true)).Should().Throw<ArgumentException>();
        service.Invoking(item => item.AddUriRule("https://example.com/*", () => false)).Should().Throw<InvalidOperationException>();
        using (var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            service.Invoking(item => item.AddUriRule("https://example.com/*", () => true)).Should().Throw<InvalidOperationException>();
        }
        new PermissionGrantManagementService(f.Store).Invoking(item => item.AddUriRule("https://example.com/*", () => true))
            .Should().Throw<InvalidOperationException>();
        preferences.Saves.Should().Be(0);
        audit.Events.Should().BeEmpty();
        var cancellation = new CancellationToken(true);
        var read = () => service.ReadAsync(ExactGrantQuery.Unfiltered, null, cancellation);
        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task UriWriteFailuresRemainExplicitAndNeverClaimPermissionSuccess()
    {
        var preferences = new UriPreferences { SaveFailure = new IOException("private") };
        var audit = new UriAudit();
        await using var f = new Fixture(uris: new PreapprovedUriConfigurationService(preferences, audit));
        f.State.UriPatternInput = "https://example.com/*";
        var add = () => f.State.AddUriRuleCommand.ExecuteAsync();
        await add.Should().ThrowAsync<IOException>();
        f.State.Busy.Should().BeFalse();
        f.State.Message.Should().NotContain("saved and read back");
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        preferences.Settings.Patterns.Should().BeEmpty();
        f.Store.Revokes.Should().Be(0);
    }

    [Theory]
    [InlineData(PermissionGrantArtifactKind.Tool, "Tool")]
    [InlineData(PermissionGrantArtifactKind.Skill, "Skill")]
    [InlineData(PermissionGrantArtifactKind.Instruction, "Instruction")]
    [InlineData(PermissionGrantArtifactKind.Agent, "Agent")]
    [InlineData(PermissionGrantArtifactKind.McpServer, "MCP server")]
    [InlineData(PermissionGrantArtifactKind.Prompt, "Prompt")]
    [InlineData(PermissionGrantArtifactKind.Uri, "URI pattern")]
    public async Task SharedRecordsExposeSafeExactMetadataAndHumanReadableLabels(PermissionGrantArtifactKind kind, string label)
    {
        await using var f = new Fixture();
        var exact = new ExactOperationPermissionGrant(Target(f.Preview, kind, CustomizationScope.Kora));
        exact.ArtifactType.Should().Be(label);
        exact.ArtifactScope.Should().Be("Kora");
        exact.Summary.Should().Be("bounded.read");
        exact.Detail.Should().Be("Perpetual | Active | revision 1");
        var uri = new UriPermissionGrant("HTTPS://EXAMPLE.COM:443");
        uri.Name.Should().Be("https://example.com/");
        uri.Summary.Should().Contain("preapproval");
        uri.Detail.Should().Contain("not an exact operation approval");
        uri.ArtifactScope.Should().Be("Unknown");
    }

    [Fact]
    public async Task OriginalClearAllRemainsUriOnlyAndPublicOperationsRejectMissingSelectionAndControl()
    {
        var preferences = new UriPreferences { Settings = PreapprovedUriSettings.Create(["https://example.com/*", "https://other.example/*"]) };
        var policy = new PreapprovedUriConfigurationService(preferences, new UriAudit());
        await using var f = new Fixture(uris: policy);
        await f.State.RefreshAsync(f.Token);
        f.State.ClearUriRulesCommand.CanExecute(null).Should().BeTrue();
        var missing = () => f.State.RemoveUriRuleAsync(f.Token);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        var exact = f.State.PermissionRows.Single(row => row.Grant is ExactOperationPermissionGrant);
        f.State.SetPermissionSelection([exact]);
        f.State.CanRemoveUriRule.Should().BeFalse();
        await missing.Should().ThrowAsync<InvalidOperationException>();
        f.State.SelectedPermission = exact;
        f.State.SetPermissionSelection([exact]);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        f.State.SelectedPermission = null;
        var uri = f.State.PermissionRows.First(row => row.Grant is UriPermissionGrant);
        f.State.SetPermissionSelection([uri]);
        f.State.CanRemoveUriRule.Should().BeFalse();
        await missing.Should().ThrowAsync<InvalidOperationException>();
        f.State.SelectedPermission = uri;
        f.State.SetPermissionSelection([uri, exact]);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        f.State.SetPermissionSelection([uri]);
        f.UriEligible = false;
        await missing.Should().ThrowAsync<InvalidOperationException>();
        var unavailable = () => f.State.AddUriRuleAsync(f.Token);
        await unavailable.Should().ThrowAsync<InvalidOperationException>();
        f.UriEligible = true;
        f.State.UriPatternInput = "https://new.example/*";
        var cancelled = () => f.State.AddUriRuleAsync(new CancellationToken(true));
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        preferences.Saves.Should().Be(0);
        await f.State.ClearUriRulesCommand.ExecuteAsync();
        policy.GetSettings().Patterns.Should().BeEmpty();
        f.State.ClearUriRulesCommand.CanExecute(null).Should().BeFalse();
        f.State.PermissionRows.Should().ContainSingle();
        f.Store.Current.Should().Be(f.Preview);
        f.Store.Revokes.Should().Be(0);
        preferences.Saves.Should().Be(1);
    }

    private static ExactGrantInspection Target(ExactGrantInspection original, PermissionGrantArtifactKind kind, CustomizationScope? scope)
    {
        var proposal = original.Grant.ApprovedProposal;
        var b = proposal.Binding;
        var binding = new ExactOperationBinding(b.ActionId, b.SourcePartition, b.SkillId, b.DefinitionDigest,
            b.DeclaredResourceDigest, b.TrackedContentDigest, b.ImplementationDigest, b.InvocationDigest, b.ResourceDigest,
            b.IdentityDigest, b.DestinationDigest, b.TransformationDigest, b.PolicyRevision, new(kind, scope));
        return original with
        {
            Grant = original.Grant with
            {
                Id = new(Guid.NewGuid()),
                ApprovedProposal = new(proposal.Request, proposal.ProposalId, proposal.Revision, binding, proposal.Effect, proposal.ExpiresAt),
            },
        };
    }

    private sealed class UriPreferences : IPreapprovedUriPreferences
    {
        internal PreapprovedUriSettings Settings { get; set; } = PreapprovedUriSettings.Empty;
        internal int Saves { get; private set; }
        internal Exception? SaveFailure { get; init; }
        public PreapprovedUriSettings Load() => Settings;
        public void Save(PreapprovedUriSettings settings)
        {
            if (SaveFailure is { } failure) { throw failure; }
            Settings = settings;
            Saves++;
        }
    }

    private sealed class UriAudit : ISecurityAuditLog
    {
        internal List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
