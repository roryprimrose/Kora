using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Fact]
    public async Task SubsequentSessionSeedsLatestDeviceModeWithoutChangingPriorSession()
    {
        using var f = new Fixture();
        ModelProviderPolicy first;
        using (var root = f.Root())
        {
            first = (await f.PolicyHost.InitializePolicyAsync(Token)).Policy!;
        }
        f.ProviderPreferences.Mode = ModelProviderMode.HostedPreferred;
        f.Request = HostRequest.Create(RequestOrigin.LocalUi);
        f.Session = new(f.Request.SessionId, new(1), true);
        f.Task = new(new(f.Request, new(1), HostTaskState.IntentRecorded), new(1), "host", true, null);
        using var next = f.Root();
        var second = (await f.PolicyHost.InitializePolicyAsync(Token)).Policy!;
        second.Mode.Should().Be(ModelProviderMode.HostedPreferred);
        second.Session.Should().NotBe(first.Session);
        first.Mode.Should().Be(ModelProviderMode.LocalOnly);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("inactive")]
    [InlineData("generation")]
    [InlineData("task")]
    [InlineData("origin")]
    [InlineData("audit-host")]
    [InlineData("audit-context")]
    public async Task InitialPolicyRequiresLiveOriginalSessionTaskAndHostAuthority(string stage)
    {
        using var f = new Fixture(stage is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        using var root = f.Root();
        if (stage is "owner") { f.Current = false; }
        if (stage is "inactive") { f.Session = f.Session with { IsActive = false }; }
        if (stage is "generation") { f.Session = f.Session with { Generation = default }; }
        if (stage is "task") { f.Task = null; }
        f.OnAudit = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            if (stage is "audit-host") { f.Current = false; }
            if (stage is "audit-context") { root.Complete(HostOperationOutcome.Completed); }
        };
        var initialized = await f.PolicyHost.InitializePolicyAsync(Token);
        initialized.Result.Outcome.Should().Be(ModelTurnOutcome.Denied);
        initialized.Policy.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(null, ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.LocalOnly, ModelProviderMode.LocalOnly)]
    [InlineData(ModelProviderMode.LocalFirst, ModelProviderMode.LocalFirst)]
    [InlineData(ModelProviderMode.HostedPreferred, ModelProviderMode.HostedPreferred)]
    public async Task InitialSessionPolicyUsesConfirmedDevicePreference(ModelProviderMode? saved, ModelProviderMode expected)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProviderPreferences.Mode = saved;
        var initialized = await f.PolicyHost.InitializePolicyAsync(Token);
        initialized.Result.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        initialized.Policy!.Mode.Should().Be(expected);
        initialized.Policy.Session.Should().Be(f.Request.SessionId);
        initialized.Policy.Revision.Value.Should().Be(1);
        f.ProviderPreferences.Mode = ModelProviderMode.LocalOnly;
        (await f.PolicyHost.InitializePolicyAsync(Token)).Policy.Should().BeSameAs(initialized.Policy);
        var turn = await f.PolicyHost.AdmitAsync(ModelTurnChoice.Local, f.Context(), null, Token);
        turn.Turn.Should().NotBeNull();
        var hosted = await f.PolicyHost.AdmitAsync(ModelTurnChoice.Hosted, f.Context(), null, Token);
        hosted.Result.Reason.Should().Be(expected == ModelProviderMode.LocalOnly ? ModelTurnReason.InvalidPolicy
            : expected == ModelProviderMode.LocalFirst ? ModelTurnReason.HandoffReviewRequired : ModelTurnReason.EgressNotAdmitted);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task HostedPreferredWithoutAdapterIsUnavailableAndNeverDispatchesOrOffersAutomaticHandoff()
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProviderPreferences.Mode = ModelProviderMode.HostedPreferred;
        var production = f.Production();
        var initialized = await production.InitializePolicyAsync(Token);
        initialized.Policy!.Mode.Should().Be(ModelProviderMode.HostedPreferred);
        var admission = await production.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        admission.Result.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        admission.Result.Reason.Should().Be(ModelTurnReason.QualificationPending);
        admission.Result.Provenance!.Selection.Should().Be(ModelProviderSelection.CopilotCandidate);
        admission.Turn.Should().BeNull();
        f.Adapter.Calls.Should().Be(0);
        var qualified = await f.PolicyHost.AdmitAsync(ModelTurnChoice.Default, f.Context(), null, Token);
        qualified.Result.Outcome.Should().Be(ModelTurnOutcome.DeniedEgress);
        qualified.Turn.Should().BeNull();
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("pending")]
    [InlineData("audit")]
    [InlineData("changed")]
    [InlineData("revision")]
    public async Task InvalidOrRacingPreferenceCannotPublishInitialPolicy(string stage)
    {
        using var f = new Fixture();
        using var root = f.Root();
        f.ProviderPreferences.Mode = ModelProviderMode.HostedPreferred;
        f.ProviderPreferences.Corrupt = stage is "corrupt";
        f.ProviderPreferences.Pending = stage is "pending";
        f.OnAudit = item =>
        {
            if (!string.Equals(item.ActionId, "model.policy", StringComparison.Ordinal) || item.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            if (stage is "audit") { throw new IOException(); }
            if (stage is "changed") { f.ProviderPreferences.Mode = ModelProviderMode.LocalOnly; }
            if (stage is "revision") { f.ProviderConfiguration.HoldUnavailable(); }
        };
        if (stage is "changed" or "revision")
        {
            var initialized = await f.PolicyHost.InitializePolicyAsync(Token);
            initialized.Result.Outcome.Should().Be(ModelTurnOutcome.Denied);
            initialized.Policy.Should().BeNull();
        }
        else
        {
            await f.PolicyHost.Invoking(host => host.InitializePolicyAsync(Token)).Should().ThrowAsync<Exception>();
        }
        f.OnAudit = null;
        f.ProviderPreferences.Corrupt = f.ProviderPreferences.Pending = false;
        f.ProviderPreferences.Mode = ModelProviderMode.LocalOnly;
        var repaired = await f.PolicyHost.InitializePolicyAsync(Token);
        repaired.Policy!.Revision.Value.Should().Be(1);
        repaired.Policy.Mode.Should().Be(ModelProviderMode.LocalOnly);
        f.Adapter.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ParallelInitializersPublishOnlyOnePolicyAndVolatileRevisionStillWins()
    {
        using var f = new Fixture();
        using var root = f.Root();
        var arrivals = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.BeforeRead = () =>
        {
            if (Interlocked.Increment(ref arrivals) == 4) { release.SetResult(); }
            return release.Task;
        };
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.PolicyHost.InitializePolicyAsync(Token)));
        f.BeforeRead = null;
        results.Should().OnlyContain(result => ReferenceEquals(result.Policy, results[0].Policy));
        var replacement = results[0].Policy! with { Revision = new(2), Mode = ModelProviderMode.LocalFirst };
        (await f.PolicyHost.SetPolicyAsync(replacement, Token)).Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        (await f.PolicyHost.InitializePolicyAsync(Token)).Policy.Should().BeSameAs(replacement);
    }
}
