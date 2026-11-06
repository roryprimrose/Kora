using AwesomeAssertions;

using Kora.Application.Mcp;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("discovery-failed")]
    [InlineData("admission")]
    [InlineData("control")]
    public async Task McpDiscoveryCannotIssueAnInventoryAfterFailureOrFinalAuthorityWithdrawal(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account)
        {
            ErrorMethod = string.Equals(fault, "discovery-failed", StringComparison.Ordinal) ? "resources/list" : null,
        };
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        bool Admitted()
        {
            if (f.Task!.Task.State != HostTaskState.Succeeded || HostActivity.Current is not null) { return true; }
            if (string.Equals(fault, "control", StringComparison.Ordinal)) { f.ControlRevision++; }
            return !string.Equals(fault, "admission", StringComparison.Ordinal);
        }
        var settings = source.Settings;
        await FluentActions.Awaiting(() => service.DiscoverMcpCapabilitiesAsync(new(f.Session, null),
            source.ServerId, source.Account, approvals.Present, Admitted, Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*No current inventory*");
        source.Settings.Should().BeSameAs(settings);
        broker.Disposed.Should().BeTrue();
        broker.Methods.Should().NotContain("tools/call").And.NotContain("resources/read").And.NotContain("prompts/get");
        f.CancelAudit.Events.Should().NotContain(item => item.Category == SecurityAuditCategory.ConfigurationWrite);
        if (string.Equals(fault, "discovery-failed", StringComparison.Ordinal)) { f.Task!.Task.State.Should().Be(HostTaskState.Unknown); }
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("admission")]
    [InlineData("control")]
    [InlineData("inspect")]
    [InlineData("revision")]
    [InlineData("expired")]
    [InlineData("source")]
    [InlineData("task-missing")]
    [InlineData("task-failed")]
    [InlineData("task-source")]
    [InlineData("task-generation")]
    [InlineData("task-request")]
    [InlineData("target")]
    [InlineData("inactive")]
    [InlineData("late-admission")]
    [InlineData("late-control")]
    [InlineData("cancelled")]
    public async Task McpInventorySaveRequiresTheExactIssuedLiveCompletedDiscovery(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var inventory = await service.DiscoverMcpCapabilitiesAsync(new(f.Session, null), source.ServerId,
            source.Account, approvals.Present, static () => true, Token);
        var settings = source.Settings;
        var writes = f.CancelAudit.Events.Count;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var admitted = true;
        switch (fault)
        {
            case "owner": service = CapabilityService(f, f.Host, source, broker, approvals); break;
            case "admission": admitted = false; break;
            case "control": f.CanControl = false; break;
            case "inspect": f.Inspect = false; break;
            case "revision": f.ControlRevision++; break;
            case "expired": f.Time.Now = inventory.ExpiresAt; break;
            case "source": File.AppendAllText(source.File, " "); break;
            case "task-missing": f.Task = null; break;
            case "task-failed": f.Task = f.Task! with { Task = new(f.Request, new(3), HostTaskState.Failed) }; break;
            case "task-source": f.Task = f.Task! with { CurrentSource = false }; break;
            case "task-generation": f.Task = f.Task! with { Generation = new(2) }; break;
            case "task-request": f.Task = f.Task! with { Task = new(HostRequest.Create(RequestOrigin.LocalUi), new(3), HostTaskState.Succeeded) }; break;
            case "target": f.Session = f.Session with { Generation = new(2) }; break;
            case "inactive":
                f.Session = f.Session with { IsActive = false };
                inventory = new(service, inventory.Request, new(f.Session, null), inventory.Source, inventory.Items,
                    inventory.Selection, inventory.Control, inventory.ExpiresAt, inventory.Capabilities);
                break;
            case "late-admission": f.OnRead = () => admitted = false; break;
            case "late-control": f.OnRead = () => f.ControlRevision++; break;
            case "cancelled": await cancellation.CancelAsync(); break;
        }
        var save = () => service.SaveMcpCapabilitiesAsync(inventory, inventory.Selection, false, () => admitted, cancellation.Token);
        if (string.Equals(fault, "cancelled", StringComparison.Ordinal)) { await save.Should().ThrowAsync<OperationCanceledException>(); }
        else { await save.Should().ThrowAsync<InvalidOperationException>(); }
        inventory.Consumed.Should().Be(0);
        source.Settings.Should().BeSameAs(settings);
        f.CancelAudit.Events.Should().HaveCount(writes, "a rejected inventory cannot publish a durable preference success");
        broker.Methods.Should().NotContain("tools/call").And.NotContain("resources/read").And.NotContain("prompts/get");
        broker.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task McpInventorySaveRequiresTheAuditedSettingsOwner()
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals, settingsAvailable: false);
        var inventory = await service.DiscoverMcpCapabilitiesAsync(new(f.Session, null), source.ServerId,
            source.Account, approvals.Present, static () => true, Token);
        var settings = source.Settings;
        var writes = f.CancelAudit.Events.Count;
        await FluentActions.Awaiting(() => service.SaveMcpCapabilitiesAsync(inventory, inventory.Selection, false,
            static () => true, Token)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*Audited*unavailable*");
        inventory.Consumed.Should().Be(0);
        source.Settings.Should().BeSameAs(settings);
        f.CancelAudit.Events.Should().HaveCount(writes);
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("revision")]
    [InlineData("cancellation")]
    public async Task McpInventorySaveRechecksAuthorityAfterItsFinalAwaitAndBeforePublication(string fault)
    {
        using var f = new Fixture();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var inventory = await service.DiscoverMcpCapabilitiesAsync(new(f.Session, null), source.ServerId,
            source.Account, approvals.Present, static () => true, Token);
        var settings = source.Settings;
        var writes = f.CancelAudit.Events.Count;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var afterTaskRead = false;
        var finalChecks = 0;
        f.BeforeTaskRead = () => { afterTaskRead = true; return Task.CompletedTask; };
        bool Admitted()
        {
            if (!afterTaskRead) { return true; }
            finalChecks++;
            if (string.Equals(fault, "cancellation", StringComparison.Ordinal) && finalChecks == 1) { cancellation.Cancel(); }
            if (finalChecks != 2) { return true; }
            if (string.Equals(fault, "revision", StringComparison.Ordinal)) { f.ControlRevision++; }
            return !string.Equals(fault, "admission", StringComparison.Ordinal);
        }
        var save = () => service.SaveMcpCapabilitiesAsync(inventory, inventory.Selection, false, Admitted, cancellation.Token);
        if (string.Equals(fault, "cancellation", StringComparison.Ordinal)) { await save.Should().ThrowAsync<OperationCanceledException>(); }
        else { await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*retired before publication*"); }
        inventory.Consumed.Should().Be(1);
        source.Settings.Should().BeSameAs(settings);
        f.CancelAudit.Events.Should().HaveCount(writes);
    }

    [Fact]
    public async Task McpInventoryConsumptionRejectsAConcurrentSaveThatAlreadyPassedCurrentness()
    {
        using var f = new Fixture();
        using var root = f.Root();
        using var source = new McpSource();
        var broker = new CapabilityBroker(source.Account);
        var approvals = new McpApprovals(f);
        var service = CapabilityService(f, f.Host, source, broker, approvals);
        var inventory = await service.DiscoverMcpCapabilitiesAsync(new(f.Session, null), source.ServerId,
            source.Account, approvals.Present, static () => true, Token);
        var changes = 0;
        source.Configuration.Changed += (_, _) => changes++;
        var afterTaskRead = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        f.BeforeTaskRead = () => { afterTaskRead = true; return Task.CompletedTask; };
        bool Admitted()
        {
            if (!afterTaskRead) { return true; }
            f.BeforeTaskRead = null;
            entered.SetResult();
            release.Wait(Token);
            return true;
        }
        var losingSave = Task.Run(() => service.SaveMcpCapabilitiesAsync(inventory, inventory.Selection, false, Admitted, Token), Token);
        try
        {
            await entered.Task.WaitAsync(Token);
            await service.SaveMcpCapabilitiesAsync(inventory, inventory.Selection, true, static () => true, Token);
        }
        finally { release.Set(); }
        await FluentActions.Awaiting(() => losingSave)
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been consumed*");
        changes.Should().Be(1);
        source.Settings.IncludedMcpServerIds.Should().Contain(source.ServerId);
        f.CancelAudit.Events.Where(item => item.Category == SecurityAuditCategory.ConfigurationWrite)
            .Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public void McpCapabilityPreferencesRequireASuccessfulCurrentSourceObservation()
    {
        using var source = new McpSource();
        source.Configuration.IsCapabilityEnabled(source.ServerId, McpCapabilityKind.Resource, "data://item").Should().BeTrue();
        File.Delete(source.File);
        FluentActions.Invoking(() => source.Configuration.Load()).Should().Throw<FileNotFoundException>();
        FluentActions.Invoking(() => source.Configuration.IsCapabilityEnabled(source.ServerId, McpCapabilityKind.Resource, "data://item"))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => source.Configuration.MayConsiderCategory(source.ServerId, McpCapabilityCategory.Resources))
            .Should().Throw<InvalidOperationException>();
    }
}