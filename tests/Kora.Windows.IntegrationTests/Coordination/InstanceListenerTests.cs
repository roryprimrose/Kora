using System.Collections.Concurrent;
using System.IO.Pipes;

using AwesomeAssertions;

using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests.Coordination;

public sealed class InstanceListenerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Saturated_activation_listener_accepts_another_launch_after_a_connection_retires(int retiring)
    {
        using var fixture = new ListenerFixture();
        var listener = fixture.Start();
        try
        {
            for (var index = 0; index < 4; index++)
            {
                await fixture.ConnectAsync();
                await fixture.Accepted[index].Task.WaitAsync(fixture.Token);
            }

            var reached = await Task.WhenAny(fixture.WaitingForCapacity.Task, listener);
            reached.Should().BeSameAs(fixture.WaitingForCapacity.Task,
                "four pending activations must wait for capacity, not terminate the owner listener");
            fixture.Created.Should().Be(4);
            fixture.Completed.Should().Be(0);

            fixture.Release[retiring].SetResult();
            await fixture.ConnectAsync();
            await fixture.Accepted[4].Task.WaitAsync(fixture.Token);
            fixture.Created.Should().Be(5);
            listener.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await fixture.StopAsync(listener);
        }
        await fixture.ReadResponsesAsync();
        fixture.Completed.Should().Be(5);
        fixture.Failures.Should().BeEmpty();
        fixture.ListenerFailure.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Stopping_idle_or_saturated_acceptance_does_not_close_handler_owned_connections(int active)
    {
        using var fixture = new ListenerFixture();
        var listener = fixture.Start();
        try
        {
            for (var index = 0; index < active; index++)
            {
                await fixture.ConnectAsync();
                await fixture.Accepted[index].Task.WaitAsync(fixture.Token);
            }
            if (active == 4) { await fixture.WaitingForCapacity.Task.WaitAsync(fixture.Token); }
            await fixture.CancelListenerAsync();
            var stopped = () => listener;
            await stopped.Should().ThrowAsync<OperationCanceledException>();
            fixture.Completed.Should().Be(0);
            fixture.Failures.Should().BeEmpty();
            fixture.Created.Should().Be(active == 0 ? 1 : 4);
        }
        finally
        {
            await fixture.StopAsync(listener);
        }
        await fixture.ReadResponsesAsync();
        fixture.Completed.Should().Be(active);
        fixture.Failures.Should().BeEmpty();
        fixture.ListenerFailure.Should().BeNull();
    }

    [Fact]
    public async Task Native_pipe_creation_failure_is_reported_without_retiring_an_accepted_activation()
    {
        using var fixture = new ListenerFixture(failCreationAt: 2);
        var listener = fixture.Start();
        try
        {
            await fixture.ConnectAsync();
            await fixture.Accepted[0].Task.WaitAsync(fixture.Token);
            var failed = () => listener;
            await failed.Should().ThrowAsync<IOException>().WithMessage("Fixture pipe creation failure.");
            fixture.Created.Should().Be(1);
            fixture.Completed.Should().Be(0);
        }
        finally
        {
            await fixture.StopAsync(listener);
        }
        await fixture.ReadResponsesAsync();
        fixture.Completed.Should().Be(1);
        fixture.Failures.Should().BeEmpty();
        fixture.ListenerFailure.Should().BeOfType<IOException>();
    }

    private sealed class ListenerFixture : IDisposable
    {
        private readonly string name = $"Kora.Tests.Activation.{Guid.NewGuid():N}";
        private readonly SemaphoreSlim capacity = new(4, 4);
        private readonly CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        private readonly CancellationTokenSource stopping;
        private readonly List<NamedPipeClientStream> clients = [];
        private readonly List<Task<InstanceMessage>> responses = [];
        private readonly List<Task> handlers = [];
        private int waits;
        private int accepted;
        private int created;
        private int completed;
        private readonly int failCreationAt;

        internal ListenerFixture(int failCreationAt = 0)
        {
            this.failCreationAt = failCreationAt;
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            stopping = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        }
        internal CancellationToken Token => deadline.Token;
        internal TaskCompletionSource WaitingForCapacity { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource[] Accepted { get; } = Signals();
        internal TaskCompletionSource[] Release { get; } = Signals();
        internal ConcurrentQueue<Exception> Failures { get; } = new();
        internal IOException? ListenerFailure { get; private set; }
        internal int Created => Volatile.Read(ref created);
        internal int Completed => Volatile.Read(ref completed);

        internal Task Start() => WindowsInstanceCoordinator.AcceptConnectionsAsync(
            CreatePipe(), CreatePipe, WaitForSlotAsync, Connected, stopping.Token);

        private NamedPipeServerStream CreatePipe()
        {
            if (Created + 1 == failCreationAt) { throw new IOException("Fixture pipe creation failure."); }
            var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
            Interlocked.Increment(ref created);
            return pipe;
        }

        private Task WaitForSlotAsync(CancellationToken token)
        {
            if (Interlocked.Increment(ref waits) == 5) { WaitingForCapacity.TrySetResult(); }
            return capacity.WaitAsync(token);
        }

        private void Connected(NamedPipeServerStream pipe)
        {
            var index = accepted++;
            handlers.Add(HandleAsync(pipe, index));
            Accepted[index].SetResult();
        }

        private async Task HandleAsync(NamedPipeServerStream pipe, int index)
        {
            try
            {
                var request = await InstanceProtocol.ReadAsync(pipe, Token);
                request.Kind.Should().Be("Activate");
                await Release[index].Task.WaitAsync(Token);
                await InstanceProtocol.WriteAsync(pipe, new(InstanceProtocol.Version, "Activated"), Token);
                Interlocked.Increment(ref completed);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failures.Enqueue(exception);
            }
            finally
            {
                await pipe.DisposeAsync();
                capacity.Release();
            }
        }

        internal async Task ConnectAsync()
        {
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            clients.Add(client);
            await client.ConnectAsync(Token);
            await InstanceProtocol.WriteAsync(client, new(InstanceProtocol.Version, "Activate"), Token);
            responses.Add(InstanceProtocol.ReadAsync(client, Token));
        }

        internal async Task StopAsync(Task listener)
        {
            foreach (var release in Release) { release.TrySetResult(); }
            // Stop only the listener; active protocol exchanges still own their pipes.
            await CancelListenerAsync();
            try { await listener; }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (IOException exception) { ListenerFailure = exception; }
            await Task.WhenAll(handlers);
        }

        internal Task CancelListenerAsync() => stopping.CancelAsync();

        internal async Task ReadResponsesAsync()
        {
            foreach (var pending in responses)
            {
                var response = await pending;
                response.Kind.Should().Be("Activated");
            }
        }

        public void Dispose()
        {
            foreach (var client in clients) { client.Dispose(); }
            stopping.Dispose();
            deadline.Dispose();
            capacity.Dispose();
        }

        private static TaskCompletionSource[] Signals() => Enumerable.Range(0, 5)
            .Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
    }
}
