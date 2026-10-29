using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Customizations;
using Kora.Core.Interaction;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class PermissionGrantMetadataTests
{
    [WindowsFact]
    public async Task PersistentAndSessionInventoryQueriesRemainScopedAcrossStorageReopen()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var target = f.Request.SessionId;
        var persistent = await f.GrantAsync("perpetual");
        f.Request = InteractionStorageFixture.NewRequest(target);
        await f.AdmitAsync(newSession: false);
        var session = await f.GrantAsync("session");
        f.Request = InteractionStorageFixture.NewRequest(target);
        await f.AdmitAsync(newSession: false);
        var once = await f.GrantAsync("once");
        await f.RunAsync(() => f.Authorization.ConsumeAsync(f.Request, once.Id, once.Revision, f.Token));
        f.Request = InteractionStorageFixture.NewRequest();
        await f.AdmitAsync(newSession: true);
        var otherSession = await f.GrantAsync("session");
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var settings = await f.Store.ReadExactGrantPageAsync(
            new(status: OperationGrantStatus.Active, managementView: true), null, f.Token);
        settings.Records.Select(item => item.Grant.Id).Should().Equal(persistent.Id);
        var scoped = await f.Store.ReadExactGrantPageAsync(
            new(status: OperationGrantStatus.Active, managementView: true, managementSession: target), null, f.Token);
        scoped.Records.Select(item => item.Grant.Id).Should().BeEquivalentTo([persistent.Id, session.Id]);
        var history = await f.Store.ReadExactGrantPageAsync(
            new(managementView: true, managementSession: target, includeOnceHistory: true), null, f.Token);
        history.Records.Select(item => item.Grant.Id).Should().BeEquivalentTo([persistent.Id, session.Id, once.Id])
            .And.NotContain(otherSession.Id);
    }

    [WindowsFact]
    public async Task LegacyAndTypedTargetsKeepCommittedProvenanceAcrossReopenAndExactRevocation()
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        var legacy = await f.GrantAsync("perpetual");
        var legacyJson = HostInteractionCodec.Encode(legacy);
        legacyJson.Should().NotContain("\"Target\"");
        HostInteractionCodec.Encode(HostInteractionCodec.Decode<OperationGrant>(legacyJson)).Should().Be(legacyJson);
        f.Request = InteractionStorageFixture.NewRequest(f.Request.SessionId);
        await f.AdmitAsync(newSession: false);
        var original = f.Proposal;
        var target = new PermissionGrantTarget(PermissionGrantArtifactKind.Tool, CustomizationScope.BuiltIn);
        f.Proposal = new(original.Request, original.ProposalId, original.Revision,
            InteractionStorageFixture.Binding(target: target), original.Effect, original.ExpiresAt);
        await f.PublishAsync();
        var typed = await f.GrantAsync("perpetual");
        typed.ApprovedProposal.Binding.Target.Should().Be(target);
        f.Reopen();
        await f.Store.InitializeAsync(f.Token);
        var page = await f.Store.ReadExactGrantPageAsync(null, f.Token);
        page.Records.Should().HaveCount(2);
        page.Records.Single(item => item.Grant.Id == legacy.Id).Grant.ApprovedProposal.Binding.Target.Should().BeNull();
        var inspected = (await f.Store.InspectExactGrantAsync(typed.Id, f.Token))!;
        inspected.Grant.ApprovedProposal.Binding.Target.Should().Be(target);
        await using var control = new Kora.Application.Interaction.ExactGrantControlAdmission(f.Store, f.Store, new(f.Tasks), f.Authorization);
        (await control.RevokeAsync(inspected, () => true, f.Token)).Outcome.Should().Be(HostInteractionOutcome.Revoked);
        (await f.Store.InspectExactGrantAsync(typed.Id, f.Token))!.Grant.ApprovedProposal.Binding.Target.Should().Be(target);
    }

    [Fact]
    public void InvalidPersistedTargetMetadataIsRejectedInsteadOfBecomingUnknown()
    {
        var binding = InteractionStorageFixture.Binding(target: new(PermissionGrantArtifactKind.Skill, CustomizationScope.Kora));
        var json = JsonNode.Parse(HostInteractionCodec.Encode(binding))!.AsObject();
        json["Target"]!["Kind"] = 99;
        var kind = () => HostInteractionCodec.Decode<ExactOperationBinding>(json.ToJsonString());
        kind.Should().Throw<InvalidDataException>();
        json["Target"]!["Kind"] = (int)PermissionGrantArtifactKind.Skill;
        json["Target"]!["Scope"] = 99;
        var scope = () => HostInteractionCodec.Decode<ExactOperationBinding>(json.ToJsonString());
        scope.Should().Throw<InvalidDataException>();
    }
}
