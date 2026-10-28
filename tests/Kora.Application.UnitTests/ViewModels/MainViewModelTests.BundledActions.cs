using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(BuiltInAction.LockMachine, FixedSessionAction.Lock)]
    [InlineData(BuiltInAction.ProposeShutdown, FixedSessionAction.Shutdown)]
    [InlineData(BuiltInAction.ProposeRestart, FixedSessionAction.Restart)]
    public async Task Bundled_routes_use_one_governed_owner_without_the_legacy_effect_or_bootstrap(
        BuiltInAction command, FixedSessionAction expected)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var invoked = new List<FixedSessionAction>();
        fixture.ViewModel.BindBundledActions((action, initiator, current) =>
        {
            current().Should().BeTrue();
            initiator.Should().Be(SecurityAuditInitiator.LocalUser);
            invoked.Add(action);
            return Task.CompletedTask;
        });
        await fixture.ViewModel.ExecuteAsync(fixture.Catalog.GetCommands().Single(item => item.Action == command),
            SecurityAuditInitiator.LocalUser);
        invoked.Should().Equal(expected);
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.ResponseBody.Should().NotContain("This bootstrap proves");
        fixture.ViewModel.BindBundledActions(null);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("disposed")]
    public async Task Bundled_original_source_is_revalidated_by_the_bound_owner(string change)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.BindBundledActions((_, _, current) =>
        {
            current().Should().BeTrue();
            switch (change)
            {
                case "binding": fixture.ViewModel.BindBundledActions(null); break;
                case "call": fixture.CallState.SetState(CallState.Active); break;
                case "privacy":
                    fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 10 };
                    break;
                case "disposed": fixture.ViewModel.Dispose(); break;
            }
            current().Should().BeFalse();
            return Task.CompletedTask;
        });
        await fixture.ViewModel.ExecuteAsync(fixture.Catalog.GetCommands().Single(item => item.Action == BuiltInAction.LockMachine),
            SecurityAuditInitiator.LocalUser);
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(FixedSessionAction.Shutdown)]
    [InlineData(FixedSessionAction.Restart)]
    public async Task Computer_power_preparation_reuses_real_lifecycle_joins_without_an_application_effect(FixedSessionAction action)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var sequence = new List<string>();
        var joins = 0;
        var releases = 0;
        fixture.ViewModel.BindConversationShutdown(() => { sequence.Add("unsent"); return Task.FromResult(true); }, () => true);
        fixture.ViewModel.BindInstructionEditorShutdown(() => { sequence.Add("editor"); return Task.FromResult(true); });
        fixture.ViewModel.BindCustomizationShutdown(() => { sequence.Add("customization"); return Task.FromResult(true); });
        using (var prepared = await fixture.ViewModel.PrepareBundledResourcesAsync(action, () => true, current =>
        {
            current().Should().BeTrue();
            sequence.Add("power");
            return Task.FromResult<ApplicationLifecycleShutdown?>(new(current,
                () => { joins++; return Task.CompletedTask; }, () => releases++));
        }))
        {
            prepared.Should().NotBeNull();
            prepared!.IsCurrent.Should().BeTrue();
            joins.Should().Be(0);
            await prepared.StopAndJoinAsync();
            joins.Should().Be(1);
            prepared.IsCurrent.Should().BeTrue();
            fixture.ViewModel.IsListening.Should().BeFalse();
        }
        releases.Should().Be(1);
        sequence.Should().Equal("unsent", "power", "editor", "customization");
        fixture.WindowActions.Should().BeEmpty();
        fixture.Events.Should().NotContain("process.restart").And.NotContain("session.lock");
        await fixture.ViewModel.ExitAsync();
        fixture.WindowActions.Should().Contain(WindowAction.Close);
    }

    [Theory]
    [InlineData("authoring")]
    [InlineData("conversation")]
    [InlineData("source-after-conversation")]
    [InlineData("power-cancel")]
    [InlineData("editor")]
    [InlineData("customization")]
    [InlineData("changed-review")]
    public async Task Computer_power_cancel_releases_only_its_review_and_never_joins_work(string fault)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var current = true;
        var joins = 0;
        var releases = 0;
        fixture.ViewModel.BindOwnedSkillAuthoring(null, () => Task.FromResult(fault is not "authoring"));
        fixture.ViewModel.BindConversationShutdown(() =>
        {
            if (fault is "source-after-conversation") { current = false; }
            return Task.FromResult(fault is not "conversation");
        });
        fixture.ViewModel.BindInstructionEditorShutdown(() => Task.FromResult(fault is not "editor"));
        fixture.ViewModel.BindCustomizationShutdown(() => Task.FromResult(fault is not "customization"));
        var operation = () => fixture.ViewModel.PrepareBundledResourcesAsync(FixedSessionAction.Shutdown, () => current, _ =>
            Task.FromResult<ApplicationLifecycleShutdown?>(fault is "power-cancel" ? null
                : new(() => fault is not "changed-review", () => { joins++; return Task.CompletedTask; }, () => releases++)));
        if (fault is "changed-review") { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        else { (await operation()).Should().BeNull(); }
        joins.Should().Be(0);
        releases.Should().Be(fault is "editor" or "customization" or "changed-review" ? 1 : 0);
        fixture.Events.Should().NotContain("process.restart").And.NotContain("session.lock");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lock_joins_private_resources_without_requesting_computer_power_or_stopping_submitted_work(bool retire)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var current = true;
        using var prepared = await fixture.ViewModel.PrepareBundledResourcesAsync(FixedSessionAction.Lock, () => current,
            _ => throw new InvalidOperationException("No computer power review for lock."));
        prepared.Should().NotBeNull();
        if (retire) { current = false; }
        if (retire) { await prepared!.Invoking(value => value.StopAndJoinAsync()).Should().ThrowAsync<InvalidOperationException>(); }
        else { await prepared!.StopAndJoinAsync(); }
        fixture.Session.LockCalls.Should().Be(0);
        fixture.ViewModel.IsListening.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_authority_after_power_join_is_not_reported_ready(bool original)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var current = true;
        var reviewed = true;
        using var prepared = await fixture.ViewModel.PrepareBundledResourcesAsync(FixedSessionAction.Restart, () => current, _ =>
            Task.FromResult<ApplicationLifecycleShutdown?>(new(() => reviewed, () =>
            {
                if (original) { current = false; } else { reviewed = false; }
                return Task.CompletedTask;
            }, static () => { })));
        await prepared!.Invoking(value => value.StopAndJoinAsync()).Should().ThrowAsync<InvalidOperationException>();
        prepared!.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Power_preparation_rejects_unknown_stale_and_overlapping_lifecycle_requests()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.Invoking(value => value.PrepareBundledResourcesAsync((FixedSessionAction)99, () => true,
            _ => Task.FromResult<ApplicationLifecycleShutdown?>(null))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await fixture.ViewModel.Invoking(value => value.PrepareBundledResourcesAsync(FixedSessionAction.Lock, () => false,
            _ => Task.FromResult<ApplicationLifecycleShutdown?>(null))).Should().ThrowAsync<InvalidOperationException>();
        using var held = await fixture.ViewModel.PrepareBundledResourcesAsync(FixedSessionAction.Shutdown, () => true, current =>
            Task.FromResult<ApplicationLifecycleShutdown?>(new(current, () => Task.CompletedTask, static () => { })));
        await fixture.ViewModel.Invoking(value => value.PrepareBundledResourcesAsync(FixedSessionAction.Restart, () => true,
            _ => Task.FromResult<ApplicationLifecycleShutdown?>(null))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bundled_cancellation_status_and_application_exit_join_the_owned_operation(bool statusAvailable)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var joins = 0;
        fixture.ViewModel.BindBundledActions((_, _, _) => Task.CompletedTask,
            () => { joins++; return Task.CompletedTask; }, statusAvailable ? () => "Exact bundled status" : null);
        await fixture.RunAsync("cancel shutdown");
        joins.Should().Be(1);
        await fixture.RunAsync("what power action is pending");
        if (statusAvailable) { fixture.ViewModel.ResponseBody.Should().Be("Exact bundled status"); }
        await fixture.ViewModel.ExitAsync();
        joins.Should().Be(2);
    }

    [Fact]
    public async Task Application_restart_joins_bundled_resources_before_requesting_process_replacement()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ViewModel.BindBundledActions((_, _, _) => Task.CompletedTask, async () =>
        {
            entered.SetResult();
#pragma warning disable VSTHRD003 // The explicit signal models resources ignoring a cancellation request.
            await released.Task;
#pragma warning restore VSTHRD003
        });
        var restarting = fixture.ViewModel.ExecuteAsync(
            fixture.Catalog.GetCommands().Single(item => item.Action == BuiltInAction.RestartApplication),
            SecurityAuditInitiator.LocalUser);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.Events.Should().NotContain("process.restart");
        restarting.IsCompleted.Should().BeFalse();
        released.SetResult();
        await restarting;
        fixture.Events.Should().Contain("process.restart");
    }
}