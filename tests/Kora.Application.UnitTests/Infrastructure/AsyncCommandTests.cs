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
        var command = new AsyncCommand(() => completion.Task);
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
            () => false);

        await command.ExecuteAsync();

        executions.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_restores_availability_when_work_fails()
    {
        var command = new AsyncCommand(() => Task.FromException(new IOException("failed")));

        var action = command.ExecuteAsync;

        await action.Should().ThrowAsync<IOException>();
        command.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task ICommand_Execute_starts_the_asynchronous_operation()
    {
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ICommand command = new AsyncCommand(() =>
        {
            invoked.SetResult();
            return Task.CompletedTask;
        });

        command.Execute(null);

        await invoked.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public void NotifyCanExecuteChanged_works_with_and_without_subscribers()
    {
        var command = new AsyncCommand(() => Task.CompletedTask);

        command.NotifyCanExecuteChanged();
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;
        command.NotifyCanExecuteChanged();

        notifications.Should().Be(1);
    }
}