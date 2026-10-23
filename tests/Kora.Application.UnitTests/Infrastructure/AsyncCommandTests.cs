using System.Windows.Input;

using AwesomeAssertions;

using Kora.Application.Infrastructure;

namespace Kora.Application.UnitTests.Infrastructure;

public sealed class AsyncCommandTests
{
    [Fact]
    public async Task ExecuteAsync_disables_command_until_work_completes()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncCommand(() => completion.Task, _ => { });
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;

        var execution = command.ExecuteAsync();

        command.CanExecute(null).Should().BeFalse();
        notifications.Should().Be(1);

        completion.SetResult();
        await execution;

        command.CanExecute(null).Should().BeTrue();
        notifications.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_does_nothing_when_predicate_rejects_execution()
    {
        var executions = 0;
        var command = new AsyncCommand(
            () =>
            {
                executions++;
                return Task.CompletedTask;
            },
            _ => { },
            () => false);

        await command.ExecuteAsync();

        executions.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_restores_availability_when_work_fails()
    {
        var command = new AsyncCommand(
            () => Task.FromException(new IOException("failed")),
            _ => { });

        var action = command.ExecuteAsync;

        await action.Should().ThrowAsync<IOException>();
        command.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ICommand_Execute_starts_the_asynchronous_operation()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ICommand command = new AsyncCommand(
            () =>
            {
                invoked.SetResult();
                return Task.CompletedTask;
            },
            _ => { });

        command.Execute(null);

        await invoked.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void NotifyCanExecuteChanged_works_with_and_without_subscribers()
    {
        var command = new AsyncCommand(() => Task.CompletedTask, _ => { });

        command.NotifyCanExecuteChanged();
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;
        command.NotifyCanExecuteChanged();

        notifications.Should().Be(1);
    }

    [Fact]
    public async Task ICommand_Execute_routes_failures_to_the_exception_handler()
    {
        var handled = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ICommand command = new AsyncCommand(
            () => Task.FromException(new IOException("failed")),
            handled.SetResult);

        command.Execute(null);

        var exception = await handled.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        exception.Should().BeOfType<IOException>()
            .Which.Message.Should().Be("failed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ICommand_Execute_routes_deferred_faults_and_cancellation_and_restores_availability(bool cancel)
    {
        var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handled = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("deferred failure");
        var notifications = 0;
        var command = new AsyncCommand(() => work.Task, handled.SetResult);
        command.CanExecuteChanged += (_, _) => notifications++;

        ((ICommand)command).Execute(null);
        command.CanExecute(null).Should().BeFalse();
        handled.Task.IsCompleted.Should().BeFalse();
        if (cancel) { work.SetCanceled(TestContext.Current.CancellationToken); }
        else { work.SetException(failure); }

        var exception = await handled.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        if (cancel) { exception.Should().BeAssignableTo<OperationCanceledException>(); }
        else { exception.Should().BeSameAs(failure); }
        command.CanExecute(null).Should().BeTrue();
        notifications.Should().Be(2);
    }

    [Fact]
    public void ICommand_Execute_routes_synchronous_delegate_failures()
    {
        var failure = new IOException("synchronous failure");
        Exception? reported = null;
        var command = new AsyncCommand(() => throw failure, exception => reported = exception);

        ((ICommand)command).Execute(null);

        reported.Should().BeSameAs(failure);
        command.CanExecute(null).Should().BeTrue();
    }
}