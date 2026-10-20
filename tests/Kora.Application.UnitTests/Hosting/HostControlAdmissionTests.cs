using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

[Collection("Host tracing")]
public sealed class HostControlAdmissionTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public HostControlAdmissionTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("missing")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("foreign")]
    [InlineData("equal-copy")]
    [InlineData("system")]
    public async Task RunExactRejectsMissingRetiredForeignOrNonUserAuthorityBeforeCallingTheOperation(string state)
    {
        var store = new AudioControlTestStore();
        await using var admission = new ExactAdmission(store);
        var request = HostRequest.Create(state is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        using var root = state is "missing" ? null
            : HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (state is "completed") { root!.Complete(HostOperationOutcome.Completed); }
        if (state is "stopped")
        {
            root!.Activity!.Stop();
            Activity.Current = root.Activity;
        }
        var proposed = state switch
        {
            "foreign" => HostRequest.Create(request.Origin),
            "equal-copy" => new(request.RequestId, request.SessionId, request.TaskId, request.Origin),
            _ => request,
        };
        var calls = 0;
        var run = () => admission.RunAsync(proposed, static () => true, (_, _) =>
        {
            calls++;
            return Task.FromResult(true);
        }, Token);

        await run.Should().ThrowAsync<InvalidOperationException>();

        calls.Should().Be(0);
        store.Tasks.Should().BeEmpty();
        if (state is "stopped") { HostActivity.Current.Should().BeNull(); }
        else { HostActivity.Current.Should().BeSameAs(root); }
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task RunExactPreservesIdentityLinksAndOutcomeWithoutCreatingASession(RequestOrigin origin)
    {
        var store = new AudioControlTestStore();
        await using var admission = new ExactAdmission(store);
        var request = HostRequest.Create(origin);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        HostActivity? owned = null;
        var result = await admission.RunAsync(request, static () => true, (eligible, token) =>
        {
            owned = HostActivity.RequireCurrent();
            owned.Request.Should().BeSameAs(request);
            owned.Should().NotBeSameAs(root);
            owned.Activity!.Links.Should().ContainSingle().Which.Context.Should().Be(root.Activity!.Context);
            eligible().Should().BeTrue();
            token.IsCancellationRequested.Should().BeFalse();
            return Task.FromResult(true);
        }, Token);

        result.Should().BeTrue();
        owned!.Outcome.Should().Be(HostOperationOutcome.Completed);
        owned.Activity!.IsStopped.Should().BeTrue();
        HostActivity.Current.Should().BeSameAs(root);
        store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunExactTerminatesItsActivityAndReleasesTheGateAfterOperationFailure(bool cancel)
    {
        var store = new AudioControlTestStore();
        await using var admission = new ExactAdmission(store);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        Exception failure = cancel ? new OperationCanceledException(Token) : new IOException("operation failed");
        HostActivity? owned = null;
        var run = () => admission.RunAsync(request, static () => true, (_, _) =>
        {
            owned = HostActivity.RequireCurrent();
            return Task.FromException<bool>(failure);
        }, Token);

        var thrown = await run.Should().ThrowAsync<Exception>();

        thrown.Which.Should().BeSameAs(failure);
        owned!.Outcome.Should().Be(cancel ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        owned.Activity!.IsStopped.Should().BeTrue();
        HostActivity.Current.Should().BeSameAs(root);
        (await admission.RunAsync(request, static () => true, static (eligible, _) => Task.FromResult(eligible()), Token))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("eligibility")]
    [InlineData("evidence")]
    [InlineData("cancel")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("disposed")]
    public async Task RunExactCapturedAdmissionFailsClosedWhenItsLiveBoundaryChanges(string change)
    {
        var store = new AudioControlTestStore();
        await using var admission = new ExactAdmission(store);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var cancellation = new CancellationTokenSource();
        var allowed = true;
        Task? disposal = null;
        var result = await admission.RunAsync(request, () => allowed, async (eligible, token) =>
        {
            eligible().Should().BeTrue();
            if (change is "eligibility") { allowed = false; }
            if (change is "evidence") { admission.MarkEvidenceUnavailable(); }
            if (change is "cancel") { cancellation.Cancel(); }
            if (change is "stopped") { root.Activity!.Stop(); }
            if (change is "completed") { root.Complete(HostOperationOutcome.Completed); }
            if (change is "disposed")
            {
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = token.Register(cancelled.SetResult);
                disposal = admission.DisposeAsync().AsTask();
                disposal.IsCompleted.Should().BeFalse();
                await cancelled.Task.WaitAsync(Token);
                token.IsCancellationRequested.Should().BeTrue();
            }
            return eligible();
        }, cancellation.Token);

        result.Should().BeFalse();
        if (disposal is not null) { await disposal; }
        store.Tasks.Should().BeEmpty();
    }

    private sealed class ExactAdmission(AudioControlTestStore store) : HostControlAdmission(store, new(store))
    {
        internal Task<bool> RunAsync(HostRequest request, Func<bool> eligible,
            Func<Func<bool>, CancellationToken, Task<bool>> operation, CancellationToken token) =>
            RunExactAsync(request, eligible, operation,
                result => result ? HostOperationOutcome.Completed : HostOperationOutcome.Failed, token);

        internal void MarkEvidenceUnavailable() => HoldEvidenceUnavailable();

        protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
            HostRequest request, Func<bool> eligible, CancellationToken token) =>
            throw new InvalidOperationException("Exact reviewed admission must not create a session.");
    }
}
