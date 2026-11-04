using System.Collections.Concurrent;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ConversationShutdownJoinsAllSubmittedWorkersBeforeReportingCancellationCallbackFailure(int callbacks)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        var logger = new ConversationShutdownLogger();
        var workspace = new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance);
        var service = new SessionConversationService(f, f, workspace, f, host, f.Time, logger, host, f.CancelAudit);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var active = await service.SubmitQueuedAsync(target, "active private original", () => true, Token);
        var pending = await service.SubmitQueuedAsync(target, "pending private original", () => true, Token);
        var failures = Enumerable.Range(0, callbacks).Select(_ => new IOException("private callback failure")).ToArray();
        var registrations = failures.Select(failure => f.Adapter.RunToken.Register(() => throw failure)).ToArray();
        var disposal = service.DisposeAsync().AsTask();
        Exception? disposalFailure;
        bool workersJoinedAtDisposal;
        try
        {
            var reached = await Task.WhenAny(disposal, logger.CancellationFailure.Task).WaitAsync(Token);
            reached.Should().BeSameAs(logger.CancellationFailure.Task,
                "callback failure must be diagnosed without terminating the owner before its physical joins");
            disposal.IsCompleted.Should().BeFalse("the original provider is still physically held");
            service.DisposeAsync().AsTask().Should().BeSameAs(disposal);
            active.Completion.IsCompleted.Should().BeFalse();
            host.IsQuiescent.Should().BeFalse();
            f.ExecutionCompletions.Should().Be(0);
            f.Adapter.Calls.Should().Be(1);
            await service.Invoking(value => value.SubmitQueuedAsync(target, "refused after shutdown", () => true, Token))
                .Should().ThrowAsync<ObjectDisposedException>();
            await service.Invoking(value => value.ReadPendingConversationAsync(target, () => true, Token))
                .Should().ThrowAsync<ObjectDisposedException>();
        }
        finally
        {
            provider.TrySetResult(LateAnswer());
            disposalFailure = await Record.ExceptionAsync(() => disposal);
            workersJoinedAtDisposal = active.Completion.IsCompleted && pending.Completion.IsCompleted;
            foreach (var registration in registrations) { await registration.DisposeAsync(); }
            await active.Completion;
            await pending.Completion;
        }
        var aggregate = disposalFailure.Should().BeOfType<AggregateException>().Subject;
        aggregate.Flatten().InnerExceptions.Should().BeEquivalentTo(failures);
        var repeatedFailure = await Record.ExceptionAsync(async () => await service.DisposeAsync());
        repeatedFailure.Should().BeSameAs(disposalFailure);
        workersJoinedAtDisposal.Should().BeTrue();
        (await active.Completion).Failure.Should().NotBeNull();
        (await pending.Completion).Failure.Should().NotBeNull();
        host.IsQuiescent.Should().BeTrue();
        f.Adapter.Calls.Should().Be(1);
        f.PendingRecords.Values.Should().OnlyContain(item => item.Message.AssistantResponse == null);
        f.PendingRecords[pending.Receipt.Message.Request.RequestId].Message.Execution.Should().BeNull();
        logger.Failures.Should().Contain(nameof(AggregateException))
            .And.NotContain("private callback failure")
            .And.NotContain("active private original")
            .And.NotContain("pending private original");
    }

    private sealed class ConversationShutdownLogger : ILogger<SessionConversationService>
    {
        internal TaskCompletionSource CancellationFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentQueue<string> Failures { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != 4310 || state is not IEnumerable<KeyValuePair<string, object?>> properties) { return; }
            var failure = properties.Single(item => item.Key is "FailureType").Value as string;
            if (failure is null) { return; }
            Failures.Enqueue(failure);
            if (failure is nameof(AggregateException)) { CancellationFailure.TrySetResult(); }
        }
    }
}
