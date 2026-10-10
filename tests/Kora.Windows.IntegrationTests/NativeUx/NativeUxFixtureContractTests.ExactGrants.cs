using System.Text.Json;

using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.LogicalTree;
using Avalonia.Automation;
using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Interaction;
using Kora.NativeUxFixture;
using Kora.Controls;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.NativeUx;

public sealed partial class NativeUxFixtureContractTests
{
    [Fact]
    public async Task Exact_grants_require_the_separate_opt_in_and_refuse_mixed_modes()
    {
        NativeUxFixtureHost.TryGetScratchParent(
            ["--launch-native-fixtures", "--scratch-parent", Path.GetTempPath(), "--exact-grants-fixture"], out _).Should().BeTrue();
        NativeUxFixtureHost.TryGetScratchParent(
            ["--exact-grants-fixture", "--scratch-parent", Path.GetTempPath()], out _).Should().BeFalse();
        NativeUxFixtureHost.TryGetScratchParent(
            ["--launch-native-fixtures", "--scratch-parent", Path.GetTempPath(), "--exact-grants-fixture", "--list-overflow-fixture"], out _).Should().BeFalse();
        var mixed = () => new NativeUxFixtureSession(Path.GetTempPath(), listOverflow: true, exactGrants: true);
        mixed.Should().Throw<ArgumentException>();
        using var original = new NativeUxFixtureSession(Path.GetTempPath());
        original.ExactGrants.Should().BeFalse();
        original.ExactGrantControl.Should().BeNull();
        var absent = () => original.ObserveExactGrantsAsync();
        await absent.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Exact_grants_seed_genuine_authority_page_passively_fence_staleness_and_deny_after_single_revoke()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath(), exactGrants: true);
            await fixture.InitializeAsync();
            using var state = new ExactGrantsViewModel(fixture.Interactions, fixture.ExactGrantControl!, fixture.Access,
                () => fixture.Access.Open, NullLogger<ExactGrantsViewModel>.Instance);
            var before = await fixture.ObserveExactGrantsAsync();
            before.Count.Should().Be(51);
            before.Pages.Should().BeGreaterThan(1);
            before.Roles.Should().OnlyContain(role => role.Inspection.Grant.Status == OperationGrantStatus.Active
                && role.Inspection.Grant.Revision.Value == 1 && role.Inspection.Grant.UseCount == 0);
            fixture.Main.HasVoiceConsent.Should().BeFalse();
            var premature = () => fixture.DenyRevokedExactConsumeAsync();
            await premature.Should().ThrowAsync<InvalidOperationException>();

            async Task SelectAsync(OperationGrant grant)
            {
                await state.RefreshAsync();
                while (!state.Records.Any(item => item.Grant.Id == grant.Id)) { await state.NextAsync(); }
                state.Selected = state.Records.Single(item => item.Grant.Id == grant.Id);
                await state.InspectAsync();
            }
            await state.RefreshAsync();
            state.Selected = state.Records[0];
            await state.InspectAsync();
            var window = new ExactGrantsWindow(state);
            window.Show();
            try
            {
                var records = state.Records;
                var selection = state.Selected;
                var inspection = state.Inspection;
                var list = window.GetLogicalDescendants().OfType<ListBox>().Single();
                list.Items.Count.Should().Be(records.Length);
                foreach (var (label, displayed) in new[]
                {
                    ("Current exact grant inspection", inspection),
                    ("Exact grant operation status", state.Message),
                })
                {
                    var text = window.GetLogicalDescendants().OfType<NamedTextBlock>().Single(control =>
                        string.Equals(AutomationProperties.GetName(control), label, StringComparison.Ordinal));
                    ControlAutomationPeer.CreatePeerForElement(text)!.GetName().Should().Be(label + ": " + displayed);
                }
                window.UpdateLayout();
                var provider = ControlAutomationPeer.CreatePeerForElement(list)!.GetProvider<IScrollProvider>()!;
                if (provider.VerticallyScrollable)
                {
                    provider.VerticalViewSize.Should().BeGreaterThan(0).And.BeLessThan(100);
                    provider.SetScrollPercent(-1, 100);
                    list.UpdateLayout();
                    provider.VerticalScrollPercent.Should().BeApproximately(100, 0.01);
                }
                else
                {
                    provider.VerticalScrollPercent.Should().Be(-1);
                    TestContext.Current.TestOutputHelper!.WriteLine(
                        "Headless exact list exposes no genuine overflow. Native overflow acceptance remains Pending, not Pass.");
                }
                state.Records.Should().Equal(records);
                state.Selected.Should().BeSameAs(selection);
                state.Inspection.Should().Be(inspection);
            }
            finally { window.Close(); }
            await SelectAsync(fixture.ExactTarget);
            state.Reviewed = true;
            state.CanRevoke.Should().BeTrue();
            state.Selected = null;
            state.CanRevoke.Should().BeFalse();
            await SelectAsync(fixture.ExactTarget);
            state.Reviewed = true;
            await state.RefreshAsync();
            state.CanRevoke.Should().BeFalse();
            JsonSerializer.Serialize(await fixture.ObserveExactGrantsAsync()).Should().Be(JsonSerializer.Serialize(before));

            await SelectAsync(fixture.ExactUseTarget);
            state.Reviewed = true;
            await fixture.ConsumeExactUseTargetAsync();
            await state.RevokeAsync();
            state.Message.Should().Contain("stale");
            var used = (await fixture.Interactions.InspectExactGrantAsync(fixture.ExactUseTarget.Id, fixture.Token))!.Grant;
            used.Revision.Value.Should().Be(2);
            used.UseCount.Should().Be(1);
            used.Status.Should().Be(OperationGrantStatus.Active);
            var repeated = () => fixture.ConsumeExactUseTargetAsync();
            await repeated.Should().ThrowAsync<InvalidOperationException>();

            await SelectAsync(fixture.ExactLifecycleTarget);
            state.Reviewed = true;
            await fixture.AdvanceExactLifecycleAsync();
            await state.RevokeAsync();
            state.Message.Should().Contain("stale");
            await SelectAsync(fixture.ExactTarget);
            state.Reviewed = true;
            fixture.Access.SetOpen(false);
            state.CanRevoke.Should().BeFalse();
            var denied = () => state.RevokeAsync();
            await denied.Should().ThrowAsync<InvalidOperationException>();
            fixture.Access.SetOpen(true);
            await fixture.Main.RefreshCommand.ExecuteAsync();
            fixture.Main.ShowApplication();
            state.CanRevoke.Should().BeFalse();
            await SelectAsync(fixture.ExactTarget);
            state.Reviewed = true;
            await state.RevokeAsync();
            state.Message.Should().Contain("committed and read back").And.Contain("not stopped or rolled back");
            await fixture.DenyRevokedExactConsumeAsync();
            var after = await fixture.ObserveExactGrantsAsync();
            after.ConsumeOutcome.Should().Be(HostInteractionOutcome.Conflict);
            after.UnrelatedSha256.Should().Be(before.UnrelatedSha256);
            var target = after.Roles.Single(role => string.Equals(role.Name, "target", StringComparison.Ordinal)).Inspection.Grant;
            target.Status.Should().Be(OperationGrantStatus.Revoked);
            target.Revision.Value.Should().Be(2);
            target.UseCount.Should().Be(0);
            after.Roles.Single(role => string.Equals(role.Name, "target", StringComparison.Ordinal)).Retention.State
                .Should().Be(before.Roles.Single(role => string.Equals(role.Name, "target", StringComparison.Ordinal)).Retention.State);
            after.Roles.Single(role => string.Equals(role.Name, "perpetual", StringComparison.Ordinal))
                .Should().Be(before.Roles.Single(role => string.Equals(role.Name, "perpetual", StringComparison.Ordinal)));
            state.Dispose();
            state.Records.Should().BeEmpty();
            state.Inspection.Should().NotContain(target.Id.Value.ToString("D"));
        });
    }

    [Fact]
    public async Task Exact_native_shell_is_constructed_only_in_headless_session()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new ExactGrantsWindow();
            window.IsVisible.Should().BeFalse();
            window.Content.Should().BeOfType<Grid>();
            window.Title.Should().Be("Exact operation grants");
            return Task.CompletedTask;
        });
    }
}
