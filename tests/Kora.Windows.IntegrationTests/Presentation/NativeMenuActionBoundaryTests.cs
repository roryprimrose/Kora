using System.Diagnostics;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed class NativeMenuActionBoundaryTests
{
    [Fact]
    public void Native_menu_action_returns_before_running_and_stays_on_the_dispatching_thread()
    {
        using var listener = ObserveActivities();
        Action? scheduled = null;
        var ran = false;
        var thread = Environment.CurrentManagedThreadId;

        SystemTrayController.RunAfterNativeMenuCloses(() =>
        {
            ran = true;
            Environment.CurrentManagedThreadId.Should().Be(thread);
            HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.LocalUi);
        }, continuation => scheduled = continuation);

        ran.Should().BeFalse();
        scheduled.Should().NotBeNull();
        scheduled!();
        ran.Should().BeTrue();
        HostActivity.Current.Should().BeNull();
    }

    [Fact]
    public void Deferred_menu_action_links_its_original_request_without_reusing_a_retired_parent()
    {
        using var listener = ObserveActivities();
        Action? scheduled = null;
        Activity? deferred = null;
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        ActivityContext parentContext;
        using (var parent = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request))
        {
            parentContext = parent.Activity!.Context;
            SystemTrayController.RunAfterNativeMenuCloses(() =>
            {
                HostActivity.RequireCurrent().Request.Should().BeSameAs(request);
                deferred = HostActivity.RequireCurrent().Activity;
                deferred!.ParentSpanId.Should().Be(default(ActivitySpanId));
                deferred.Links.Should().ContainSingle().Which.Context.Should().Be(parentContext);
            }, continuation => scheduled = continuation);
            parent.Complete(HostOperationOutcome.Completed);
        }

        scheduled!();

        deferred.Should().NotBeNull();
        deferred!.Status.Should().Be(ActivityStatusCode.Ok);
        deferred.IsStopped.Should().BeTrue();
        HostActivity.Current.Should().BeNull();
    }

    [Fact]
    public void Deferred_menu_action_propagates_failure_and_terminates_its_activity()
    {
        using var listener = ObserveActivities();
        Action? scheduled = null;
        Activity? deferred = null;
        var expected = new InvalidOperationException("Synthetic menu action failure.");
        SystemTrayController.RunAfterNativeMenuCloses(() =>
        {
            deferred = HostActivity.RequireCurrent().Activity;
            throw expected;
        }, continuation => scheduled = continuation);

        var invoke = () => scheduled!();

        invoke.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        deferred!.Status.Should().Be(ActivityStatusCode.Error);
        deferred.IsStopped.Should().BeTrue();
        HostActivity.Current.Should().BeNull();
    }

    private static ActivityListener ObserveActivities()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
