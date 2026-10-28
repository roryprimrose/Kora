using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Context;

public sealed partial class ClipboardExplanationWorkflowTests
{
    [Theory]
    [InlineData("once", 2, RequestOrigin.LocalUi)]
    [InlineData("session", 1, RequestOrigin.LocalUi)]
    [InlineData("perpetual", 1, RequestOrigin.LocalUi)]
    [InlineData("once", 2, RequestOrigin.ActivatedVoice)]
    public async Task EveryNativeReadConsumesItsOwnApplicableGrantBeforeTouchingClipboard(
        string scope, int questions, RequestOrigin origin)
    {
        await using var fixture = new Fixture(productionSourcePolicy: true, productionReadPolicy: true);
        using var root = fixture.Root(origin);
        fixture.GrantChoice = scope;
        var readsBeforeRequest = 0;
        fixture.BeforePermissionResult = () =>
        {
            fixture.Reader.Calls.Should().Be(readsBeforeRequest);
            fixture.Broker.Current.Should().BeNull();
        };
        for (var index = 0; index < 2; index++)
        {
            readsBeforeRequest = index;
            (await fixture.Action.ExecuteAsync(null, () => true, fixture.Token)).Outcome
                .Should().Be(ClipboardExplanationOutcome.ReviewRequired);
            fixture.Reader.Calls.Should().Be(index + 1);
            fixture.Tasks.Last().State.Should().Be(HostTaskState.Succeeded);
            fixture.Adapter.Calls.Should().Be(0);
            fixture.Broker.Clear();
        }
        fixture.PermissionQuestions.Should().Be(questions);
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("unavailable")]
    [InlineData("invalid-receipt")]
    [InlineData("changed")]
    public async Task NoGrantOrChangedAdmissionMeansZeroClipboardReads(string reason)
    {
        await using var fixture = new Fixture(productionReadPolicy: true, bindPermissionPresenter: reason is not "unavailable");
        using var root = fixture.Root();
        var current = true;
        if (reason is "denied") { fixture.GrantChoice = null; }
        if (reason is "invalid-receipt") { fixture.ChangePermissionResult = result => result with { Admission = null }; }
        if (reason is "changed") { fixture.BeforePermissionResult = () => current = false; }
        (await fixture.ReadAction.ExecuteAsync(() => current, fixture.Token)).Should().Be(ClipboardOutcome.Denied);
        fixture.Reader.Calls.Should().Be(0);
        fixture.Broker.Current.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(0);
        fixture.Tasks.Last().State.Should().Be(HostTaskState.Failed);
    }

    [Theory]
    [InlineData("grant", ClipboardOutcome.Unavailable)]
    [InlineData("expiry", ClipboardOutcome.Unavailable)]
    [InlineData("clear", ClipboardOutcome.Cancelled)]
    [InlineData("unsupported", ClipboardOutcome.UnsupportedFormat)]
    public async Task ReadbackRevalidatesAuthorityBeforePublishingSnapshot(string change, ClipboardOutcome expected)
    {
        await using var fixture = new Fixture(productionReadPolicy: true);
        using var root = fixture.Root();
        fixture.Reader.BeforeReturn = () =>
        {
            if (change is "grant") { RevokePermission(fixture); }
            if (change is "expiry") { fixture.Time.Now += ClipboardExplanationContext.Lifetime; }
            if (change is "clear") { fixture.Broker.Clear(); }
        };
        if (change is "unsupported") { fixture.Reader.Outcome = ClipboardOutcome.UnsupportedFormat; }
        (await fixture.ReadAction.ExecuteAsync(() => true, fixture.Token)).Should().Be(expected);
        fixture.Reader.Calls.Should().Be(1);
        fixture.Broker.Current.Should().BeNull();
        fixture.Adapter.Calls.Should().Be(0);
        await fixture.Broker.WaitForQuiescenceAsync();
        fixture.Broker.IsQuiescent.Should().BeTrue();
        fixture.Tasks.Last().State.Should().Be(change is "clear" ? HostTaskState.Unknown : HostTaskState.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadGrantProducerCannotAdmitNonoriginalOrIneligibleInput(bool nonoriginal)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root(nonoriginal ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        (await fixture.GrantedPolicy.RequestReadAsync(() => nonoriginal, fixture.Token)).Should().BeNull();
        fixture.PermissionQuestions.Should().Be(0);
        fixture.Reader.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem, true)]
    [InlineData(RequestOrigin.LocalUi, false)]
    public async Task UnadmittedReadCannotCreateTaskOrRequestPermission(RequestOrigin origin, bool eligible)
    {
        await using var fixture = new Fixture(productionReadPolicy: true);
        using var root = fixture.Root(origin);
        (await fixture.ReadAction.ExecuteAsync(() => eligible, fixture.Token)).Should().Be(ClipboardOutcome.Denied);
        fixture.Tasks.Should().BeEmpty();
        fixture.PermissionQuestions.Should().Be(0);
        fixture.Reader.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledPermissionWaitCannotLeaveAnUnfinishedReadTask(bool callerCancellation)
    {
        await using var fixture = new Fixture(productionReadPolicy: true);
        using var root = fixture.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
        fixture.GrantedPolicy.BindPresenter((_, _, _, _) =>
        {
            if (callerCancellation) { cancellation.Cancel(); }
            throw new OperationCanceledException("Synthetic stopped permission wait");
        });
        var read = () => fixture.ReadAction.ExecuteAsync(() => true, cancellation.Token);
        if (callerCancellation)
        {
            (await read()).Should().Be(ClipboardOutcome.Cancelled);
            fixture.Tasks.Last().State.Should().Be(HostTaskState.Unknown);
        }
        else
        {
            await read.Should().ThrowAsync<InvalidOperationException>();
            fixture.Tasks.Last().State.Should().Be(HostTaskState.Failed);
        }
        fixture.Reader.Calls.Should().Be(0);
        fixture.Broker.Current.Should().BeNull();
        fixture.Broker.IsQuiescent.Should().BeTrue();
    }
}
