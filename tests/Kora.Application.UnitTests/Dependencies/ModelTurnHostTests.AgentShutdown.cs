using System.Text;

using AwesomeAssertions;

using Kora.Application.Agents;
using Kora.Application.Dependencies;
using Kora.Core.Agents;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentShutdownJoinsSourceAndModelAfterCancellationSubscriberFailure(bool failQuiescence)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var host = QualifiedHistoryHost(f);
        var reader = new ShutdownAgentReader();
        var execution = new ShutdownAgentExecution { FailQuiescence = failQuiescence, HoldQuiescence = true };
        var workflow = new BoundedAgentWorkflow(reader, f, f, f.ConversationService(host, execution: host),
            execution, f, f.Time);
        var reviewing = workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        await reader.Entered.Task.WaitAsync(Token);
        var disposing = workflow.DisposeAsync().AsTask();
        try
        {
            await reader.CancellationObserved.Task.WaitAsync(Token);
            reader.Released.Should().BeFalse();
            reviewing.IsCompleted.Should().BeFalse();
        }
        finally { reader.Release.TrySetResult(); }
        await reviewing.Invoking(value => value).Should().ThrowAsync<Exception>();
        try
        {
            await Task.WhenAny(execution.Entered.Task, disposing).WaitAsync(Token);
            execution.Entered.Task.IsCompleted.Should().BeTrue();
            disposing.IsCompleted.Should().BeFalse();
        }
        finally { execution.Release.TrySetResult(); }
        var failure = await disposing.Invoking(value => value).Should().ThrowAsync<AggregateException>();
        failure.Which.Flatten().InnerExceptions.Select(item => item.Message).Should()
            .Contain("Source cancellation subscriber failed.");
        if (failQuiescence)
        {
            failure.Which.Flatten().InnerExceptions.Select(item => item.Message).Should()
                .Contain("Model quiescence inspection failed.");
        }
        execution.Joins.Should().Be(1);
        execution.JoinToken.CanBeCanceled.Should().BeFalse();
        reader.Released.Should().BeTrue();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        f.Audits.Should().BeEmpty();
        await workflow.Invoking(value => value.ReviewAsync("selected.agent.md", new(f.Session, null), "replacement",
            RequestOrigin.LocalUi, () => true, Token)).Should().ThrowAsync<ObjectDisposedException>();
        workflow.DisposeAsync().AsTask().Should().BeSameAs(disposing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AgentShutdownReportsSourceReleaseFailureAfterJoiningModel(bool failRelease)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var host = QualifiedHistoryHost(f);
        var reader = new AgentReader();
        var execution = new ShutdownAgentExecution();
        var source = new ShutdownReleaseReader(reader, failRelease);
        var workflow = new BoundedAgentWorkflow(source, f, f, f.ConversationService(host, execution: host),
            execution, f, f.Time);
        using var review = await workflow.ReviewAsync("selected.agent.md", new(f.Session, null), "original task",
            RequestOrigin.LocalUi, () => true, Token);
        var disposing = workflow.DisposeAsync().AsTask();
        if (failRelease)
        {
            await disposing.Invoking(value => value).Should().ThrowAsync<IOException>()
                .WithMessage("Agent source release failed.");
        }
        else { await disposing; }
        reader.Released.Should().BeTrue();
        review.IsInvocationReady.Should().BeFalse();
        execution.Joins.Should().Be(1);
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        workflow.DisposeAsync().AsTask().Should().BeSameAs(disposing);
    }

    private sealed class ShutdownAgentReader : IAgentProfileReader, IAgentProfileSelection
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Released { get; private set; }
        public LocalFileMetadata Metadata { get; } =
            new(Path.GetFullPath("selected.agent.md"), "shutdown-source", 512, DateTimeOffset.UnixEpoch);

        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token) =>
            Task.FromResult<IAgentProfileSelection>(this);

        public async Task<byte[]> ReadAsync(CancellationToken token)
        {
            using var registration = token.Register(() =>
            {
                CancellationObserved.TrySetResult();
                throw new IOException("Source cancellation subscriber failed.");
            });
            Entered.TrySetResult();
            await Release.Task.WaitAsync(Token);
            return Encoding.UTF8.GetBytes(AgentProfile);
        }

        public void Dispose() => Released = true;
    }

    private sealed class ShutdownReleaseReader(AgentReader reader, bool failRelease) : IAgentProfileReader, IAgentProfileSelection
    {
        public LocalFileMetadata Metadata => reader.Metadata;
        public Task<IAgentProfileSelection> OpenAsync(string path, CancellationToken token) =>
            Task.FromResult<IAgentProfileSelection>(this);
        public Task<byte[]> ReadAsync(CancellationToken token) => reader.ReadAsync(token);
        public void Dispose()
        {
            reader.Dispose();
            if (failRelease) { throw new IOException("Agent source release failed."); }
        }
    }

    private sealed class ShutdownAgentExecution : IModelTurnExecution
    {
        internal bool FailQuiescence { get; init; }
        internal bool HoldQuiescence { get; init; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Joins { get; private set; }
        internal CancellationToken JoinToken { get; private set; }
        public Task<ModelTurnResult> RunAsync(ModelTurn turn, Func<bool> admitted, CancellationToken token,
            Func<CancellationToken, Task>? validateSource = null) =>
            throw new InvalidOperationException("Shutdown cannot dispatch a model turn.");
        public async Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
        {
            Joins++;
            JoinToken = cancellationToken;
            Entered.TrySetResult();
            if (HoldQuiescence) { await Release.Task.WaitAsync(Token); }
            if (FailQuiescence) { throw new IOException("Model quiescence inspection failed."); }
        }
    }
}
