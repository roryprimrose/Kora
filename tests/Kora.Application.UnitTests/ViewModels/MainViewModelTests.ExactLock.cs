using System.Reflection;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("Revision")]
    [InlineData("OwnershipBindingRevision")]
    [InlineData("AssistantNameRevision")]
    public async Task ExactLockRejectsIncoherentCapturedInputBeforeTransferringActivation(string property)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var capture = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("CaptureManualCallInput",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var input = capture.Invoke(fixture.ViewModel, [RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision, null, false])!;
        var transfer = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("CaptureLockSource",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Func<bool>)transfer.Invoke(fixture.ViewModel, [input])!).Invoke().Should().BeTrue();
        // Supplemental envelope corruption exercises states rejected before the public router can transfer activation.
        input.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(input, -1L);
        var admitted = (Func<bool>)transfer.Invoke(fixture.ViewModel, [input])!;
        admitted().Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
        fixture.Audit.Events.Should().NotContain(item => item.ActionId == "session.lock");
    }

    private static void BindAdmittedLockResourceOwner(Fixture fixture) =>
        fixture.ViewModel.BindBundledActions(async (action, _, current) =>
        {
            action.Should().Be(FixedSessionAction.Lock);
            using var prepared = await fixture.ViewModel.PrepareBundledResourcesAsync(action, current,
                _ => throw new InvalidOperationException("Lock cannot request computer power."));
            await prepared!.StopAndJoinAsync();
            prepared.IsCurrent.Should().BeTrue();
            fixture.Session.LockCurrentSession().Should().BeTrue();
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivacyClosureJoinsTheExactOwnerWithoutPublishingItsLateReply(bool fail)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.ViewModel.BindBundledActions((_, _, _) => Task.CompletedTask, async () =>
        {
            entered.SetResult();
#pragma warning disable VSTHRD003 // The explicit barrier models the existing owner physically joining after cancellation.
            await release.Task;
#pragma warning restore VSTHRD003
            if (fail) { throw new IOException("Synthetic Lock resource join failure."); }
        });
        fixture.ViewModel.CloseForObservedPrivacyEvent("Synthetic locked session", true);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.PrivacyClosureTask.IsCompleted.Should().BeFalse();
        fixture.Session.LockCalls.Should().Be(0);
        release.SetResult();
        await fixture.ViewModel.PrivacyClosureTask;
        fixture.ViewModel.ResponseTitle.Should().Be(fail ? "Audio privacy closure needs attention." : "Windows privacy recovery");
        fixture.Session.LockCalls.Should().Be(0);
    }
}
