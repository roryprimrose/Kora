using AwesomeAssertions;
using Kora.Core.Platform;
using Kora.Core.Voice;

namespace Kora.Windows.IntegrationTests;

public sealed class DesktopCapabilityHostAccessTests
{
    [Theory]
    [InlineData("accepted")]
    [InlineData("decline")]
    [InlineData("cancel")]
    [InlineData("rebind")]
    [InlineData("failure")]
    public async Task HandoffReviewsBeforeClosingAdmissionAndNeverShutsDownAnInvalidReview(string outcome)
    {
        var bridge = new DesktopInstanceOwnershipBridge();
        var reviewed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var shutdowns = 0;
        var aborts = 0;
        bridge.BindCallbacks(_ => Task.CompletedTask, _ =>
        {
            bridge.IsCapabilityAdmissionOpen.Should().BeFalse();
            shutdowns++;
            return Task.FromResult(true);
        }, _ => { aborts++; return Task.CompletedTask; }, async _ =>
        {
            bridge.IsCapabilityAdmissionOpen.Should().BeTrue();
            reviewed.SetResult();
            var accepted = await release.Task;
            if (outcome is "failure") { throw new IOException("Synthetic review failure."); }
            return accepted;
        });
        var handoff = bridge.QuiesceForHandoffAsync(cancellation.Token);
        await reviewed.Task.WaitAsync(TestContext.Current.CancellationToken);
        (await bridge.QuiesceForHandoffAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        shutdowns.Should().Be(0);
        if (outcome is "cancel") { cancellation.Cancel(); }
        if (outcome is "rebind") { bridge.UnbindCallbacks(); }
        release.SetResult(outcome is not "decline");
        var completion = () => handoff;
        if (outcome is "cancel") { await completion.Should().ThrowAsync<OperationCanceledException>(); }
        else if (outcome is "rebind") { await completion.Should().ThrowAsync<InvalidOperationException>(); }
        else if (outcome is "failure") { await completion.Should().ThrowAsync<IOException>(); }
        else { (await handoff).Should().Be(outcome is "accepted"); }
        shutdowns.Should().Be(outcome is "accepted" ? 1 : 0);
        aborts.Should().Be(outcome is "accepted" ? 0 : 1);
    }

    [Theory]
    [InlineData(WindowsSessionState.Unknown)]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    [InlineData(WindowsSessionState.SignedOut)]
    [InlineData(WindowsSessionState.Suspended)]
    public void Unknown_or_ineligible_windows_state_cannot_acquire_discovery(WindowsSessionState state)
    {
        var bridge = new DesktopInstanceOwnershipBridge();
        bridge.BindCallbacks(_ => Task.CompletedTask, _ => Task.FromResult(true));
        using var privacy = new Privacy { State = state };
        var access = new DesktopCapabilityHostAccess(bridge, new Session(), privacy);
        access.IsCurrentHost.Should().BeFalse();
    }

    [Fact]
    public async Task Discovery_requires_bound_ownership_unlocked_state_and_no_live_handoff()
    {
        var bridge = new DesktopInstanceOwnershipBridge();
        using var privacy = new Privacy();
        var session = new Session();
        var access = new DesktopCapabilityHostAccess(bridge, session, privacy);
        access.IsCurrentHost.Should().BeFalse();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.BindCallbacks(_ => Task.CompletedTask, _ =>
        {
            entered.SetResult();
            return release.Task;
        }, _ => Task.CompletedTask);
        var admittedEpoch = bridge.AdmissionRevision;
        bridge.AdmissionRevision.Should().Be(admittedEpoch, "passive inspection does not renew admission");
        access.IsCurrentHost.Should().BeTrue();
        session.Unlocked = false;
        access.IsCurrentHost.Should().BeFalse();
        session.Unlocked = true;
        var handoff = bridge.QuiesceForHandoffAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        bridge.AdmissionRevision.Should().BeGreaterThan(admittedEpoch);
        var handoffEpoch = bridge.AdmissionRevision;
        access.IsCurrentHost.Should().BeFalse();
        release.SetResult(false);
        (await handoff).Should().BeFalse();
        bridge.AdmissionRevision.Should().BeGreaterThan(handoffEpoch);
        access.IsCurrentHost.Should().BeTrue();
        bridge.UnbindCallbacks();
        bridge.AdmissionRevision.Should().BeGreaterThan(handoffEpoch);
        access.IsCurrentHost.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_handoff_recovery_keeps_discovery_denied()
    {
        var bridge = new DesktopInstanceOwnershipBridge();
        bridge.BindCallbacks(_ => Task.CompletedTask, _ => Task.FromResult(false),
            _ => Task.FromException(new IOException("fixture recovery failure")));
        using var privacy = new Privacy();
        var access = new DesktopCapabilityHostAccess(bridge, new Session(), privacy);
        (await bridge.QuiesceForHandoffAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        bridge.IsHandoffRecoveryRequired.Should().BeTrue();
        access.IsCurrentHost.Should().BeFalse();
    }

    private sealed class Session : ISessionController
    {
        public bool Unlocked { get; set; } = true;
        public bool IsCurrentSessionUnlocked() => Unlocked;
        public bool LockCurrentSession() => throw new InvalidOperationException("Read-only tests must never lock Windows.");
    }

    private sealed class Privacy : IWindowsPrivacyObservationService
    {
        public event EventHandler<WindowsPrivacyChangedEventArgs>? Changed { add { } remove { } }
        public WindowsSessionState State { get; set; } = WindowsSessionState.Unlocked;
        public WindowsPrivacySnapshot Current => new(State, MicrophoneAccessState.Unknown, 0, [], null, null);
        public WindowsPrivacySnapshot Refresh() => Current;
        public void Dispose() { }
    }
}
