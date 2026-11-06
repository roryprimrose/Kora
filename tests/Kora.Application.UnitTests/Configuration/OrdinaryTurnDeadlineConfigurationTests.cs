using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class OrdinaryTurnDeadlineConfigurationTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public OrdinaryTurnDeadlineConfigurationTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task SaveReadbackResetAndRestartConsumeOnlyConfirmedFuturePreference(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture { Origin = origin, Initiator = initiator };
        f.Service.Capture().Should().Be(OrdinaryTurnDeadline.Default);
        var initial = await f.Refresh();
        initial.Saved.Should().BeNull();
        (await f.Save(initial, new(1))).Should().BeTrue();
        var readback = await f.Refresh();
        readback.Effective.Should().Be(new OrdinaryTurnDeadline(1));
        readback.Revision.Should().BeGreaterThan(initial.Revision);
        new LocalOrdinaryTurnDeadlinePreferences(f.Store).Load().Should().Be(new OrdinaryTurnDeadline(1));
        f.Service.Capture().Should().Be(new OrdinaryTurnDeadline(1));
        (await f.Save(readback, null)).Should().BeTrue();
        (await f.Refresh()).Effective.Should().Be(OrdinaryTurnDeadline.Default);
        f.Store.Values.Should().BeEmpty();
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Control.Tasks.Where(task => task.IsTerminal).Should().OnlyContain(task => task.State == HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("external")]
    [InlineData("revoked")]
    [InlineData("origin")]
    [InlineData("initiator")]
    public async Task StaleOrUnadmittedControlCannotWrite(string fault)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var expected = await f.Refresh();
        if (fault is "stale") { await f.Refresh(); }
        if (fault is "external") { f.Preferences.Save(new(10)); }
        if (fault is "revoked") { f.Eligible = false; }
        if (fault is "origin") { f.Initiator = SecurityAuditInitiator.VoiceCommand; }
        if (fault is "initiator") { f.Initiator = SecurityAuditInitiator.System; }
        await f.Invoking(value => value.Save(expected, new(1))).Should().ThrowAsync<InvalidOperationException>();
        f.Audit.Events.Should().BeEmpty();
        f.Preferences.Load().Should().Be(fault is "external" ? new OrdinaryTurnDeadline(10) : null);
    }

    [Theory]
    [InlineData("begin")]
    [InlineData("save")]
    [InlineData("readback")]
    [InlineData("requested-audit")]
    [InlineData("terminal-audit")]
    [InlineData("terminal-receipt")]
    [InlineData("confirm")]
    [InlineData("after-confirm")]
    [InlineData("before-confirm")]
    [InlineData("before-confirm-value")]
    [InlineData("after-confirm-value")]
    [InlineData("before-confirm-missing")]
    [InlineData("after-confirm-missing")]
    [InlineData("reentrant")]
    public async Task WriteOrReceiptFailuresNeverActivateUnconfirmedPreference(string fault)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var expected = await f.Refresh();
        f.Store.Before = (operation, name) =>
        {
            if (fault is "begin" && operation is "write" && name.Contains("unconfirmed", StringComparison.Ordinal)
                || fault is "save" && operation is "write" && name.Contains("minutes", StringComparison.Ordinal)
                || fault is "confirm" && operation is "delete")
            { throw new IOException("injected preference failure"); }
        };
        f.Store.After = (operation, name) =>
        {
            if (operation is "write" && name.Contains("minutes", StringComparison.Ordinal))
            {
                if (fault is "readback") { f.Store.Values[name] = "2"; }
                if (fault is "reentrant") { f.Service.Capture(); }
            }
            if (operation is "delete" && fault is "after-confirm") { f.Eligible = false; }
            if (operation is "delete" && fault is "after-confirm-value")
            { f.Store.Values["ordinary-turn-deadline-minutes.txt"] = "2"; }
            if (operation is "delete" && fault is "after-confirm-missing")
            { f.Store.Values.Remove("ordinary-turn-deadline-minutes.txt"); }
        };
        f.Audit.Before = item =>
        {
            if (fault is "requested-audit" && item.Outcome == SecurityAuditOutcome.Requested
                || fault is "terminal-audit" && item.Outcome == SecurityAuditOutcome.Succeeded)
            { throw new IOException("injected required audit failure"); }
        };
        if (fault is "terminal-receipt") { f.Control.FailTerminal = true; }
        if (fault is "before-confirm") { f.Control.BeforeCommit = task => { if (task.IsTerminal) { f.Eligible = false; } }; }
        if (fault is "before-confirm-value")
        { f.Control.BeforeCommit = task => { if (task.IsTerminal) { f.Store.Values["ordinary-turn-deadline-minutes.txt"] = "2"; } }; }
        if (fault is "before-confirm-missing")
        { f.Control.BeforeCommit = task => { if (task.IsTerminal) { f.Store.Values.Remove("ordinary-turn-deadline-minutes.txt"); } }; }
        await f.Invoking(value => value.Save(expected, new(1))).Should().ThrowAsync<Exception>();
        f.Store.Before = null;
        f.Store.After = null;
        if (fault is "begin" or "requested-audit") { f.Service.Capture().Should().Be(OrdinaryTurnDeadline.Default); }
        else { f.Service.Invoking(value => value.Capture()).Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoveryCannotPublishAnObservationChangedDuringTerminalReceipt(bool revoke)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        await f.Refresh();
        f.Control.BeforeCommit = task =>
        {
            if (!task.IsTerminal) { return; }
            if (revoke) { f.Eligible = false; }
            else { f.Preferences.Save(new(10)); }
        };
        await f.Invoking(value => value.Refresh()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservationCannotCrossOriginalChannelOrRevokedDiscoveryLifetime(bool changeOrigin)
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var discoverable = true;
        var expected = await f.Service.RefreshAsync(RequestOrigin.LocalUi, () => discoverable, TestContext.Current.CancellationToken);
        if (changeOrigin) { f.Origin = RequestOrigin.ActivatedVoice; }
        else { discoverable = false; }
        await f.Invoking(value => value.Save(expected, new(1))).Should().ThrowAsync<InvalidOperationException>();
        f.Store.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task AdmissionRevokedDuringRequestedAuditDeniesWithoutPreferenceWrite()
    {
        await using var f = new OrdinaryDeadlineConfigurationFixture();
        var expected = await f.Refresh();
        f.Audit.Before = item => { if (item.Outcome == SecurityAuditOutcome.Requested) { f.Eligible = false; } };
        (await f.Save(expected, new(1))).Should().BeFalse();
        f.Store.Values.Should().BeEmpty();
        f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("61")]
    [InlineData("05")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("1\n")]
    public void CorruptSavedValuesAreNotDefaults(string text)
    {
        var store = new OrdinaryDeadlineConfigurationFixture.DeadlinePreferenceStore();
        store.Values["ordinary-turn-deadline-minutes.txt"] = text;
        new LocalOrdinaryTurnDeadlinePreferences(store).Invoking(value => value.Load()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("get work.active-deadline-minutes", AppearanceCommandOperation.Get, null)]
    [InlineData("Kora, status work.active-deadline-minutes", AppearanceCommandOperation.Get, null)]
    [InlineData("set work.active-deadline-minutes to 10", AppearanceCommandOperation.Set, "10")]
    [InlineData("reset work.active-deadline-minutes", AppearanceCommandOperation.Reset, null)]
    [InlineData("set work.active-deadline-minutes", AppearanceCommandOperation.Clarify, null)]
    [InlineData("get work.active-deadline-minutes\n", AppearanceCommandOperation.Clarify, null)]
    public void CanonicalCommandsKeepRetiredQueueProductSeparate(string input, AppearanceCommandOperation operation, string? value)
    {
        var parsed = OrdinaryTurnDeadlineCommand.Parse(input, "Kora")!;
        parsed.Operation.Should().Be(operation);
        parsed.Value.Should().Be(value);
        OrdinaryTurnDeadlineCommand.Parse("get queue.active-deadline-minutes", "Kora").Should().BeNull();
        OrdinaryTurnDeadlineCommand.FixedPhrases.Should().HaveCount(3);
    }

    [Fact]
    public void OversizedDeadlineInputIsClarifiedWithoutParsingValue()
    {
        OrdinaryTurnDeadlineCommand.Parse("set work.active-deadline-minutes to " + new string('1', 1024), "Kora")!
            .Operation.Should().Be(AppearanceCommandOperation.Clarify);
    }
}
