using System.Threading.Channels;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(true, DependencyReadiness.Ready, false)]
    [InlineData(true, DependencyReadiness.Missing, false)]
    [InlineData(true, DependencyReadiness.Failed, false)]
    [InlineData(false, DependencyReadiness.Ready, false)]
    [InlineData(false, DependencyReadiness.Missing, false)]
    [InlineData(false, DependencyReadiness.Failed, false)]
    [InlineData(false, DependencyReadiness.Ready, true)]
    public async Task Original_setup_joins_final_verification_and_reports_the_owned_cancellation_or_success(
        bool cancel, DependencyReadiness readiness, bool failProbe)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Missing, "Model missing.");
        await fixture.ViewModel.InitializeAsync();
        fixture.Probe.Status = fixture.Probe.Status with { Readiness = readiness, Detail = "Final verification." };
        fixture.Probe.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var setupStates = new List<SetupTask>();
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.SetupTasks), StringComparison.Ordinal))
            {
                setupStates.AddRange(fixture.ViewModel.SetupTasks);
            }
        };
        var continuation = new HeldProbeContinuation();
        var originalContext = SynchronizationContext.Current;
        Task install;
        try
        {
            SynchronizationContext.SetSynchronizationContext(continuation);
            install = fixture.ViewModel.InstallLocalModelAsync();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        try
        {
            while (fixture.LocalModel.Calls == 0)
            {
                await continuation.RunNextAsync(TestContext.Current.CancellationToken);
            }
            fixture.LocalModel.Calls.Should().Be(1);
            fixture.ViewModel.IsLocalModelSetupActive.Should().BeTrue();
            fixture.ViewModel.SetupTasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Running);
            setupStates.Should().OnlyContain(task => task.State == SetupTaskState.Running,
                "installation is not the completion of Install and verify");
            fixture.ViewModel.LocalModelSetupStatus.Should().Be("Validating local inference readiness.");
            fixture.ViewModel.LocalModelSetupProgress.Should().NotBe(100);
            fixture.ViewModel.IsLocalModelSetupProgressIndeterminate.Should().BeTrue();
            fixture.ViewModel.CanInstallLocalModel.Should().BeFalse();
            fixture.ViewModel.RefreshCommand.CanExecute(null).Should().BeFalse();
            fixture.ViewModel.CommandText = "cancel task";
            fixture.ViewModel.RunTypedCommand.CanExecute(null).Should().BeTrue();
            if (failProbe)
            {
                fixture.Probe.Gate.SetException(new IOException("Verification failed."));
            }
            else
            {
                fixture.Probe.Gate.SetResult();
            }
            await continuation.WaitForPostAsync(TestContext.Current.CancellationToken);
            if (cancel)
            {
                await fixture.RunAsync("cancel task");
                fixture.ViewModel.ResponseTitle.Should().Be("Cancelling local model setup.");
                fixture.ViewModel.SetupTasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Running);
            }
            install.IsCompleted.Should().BeFalse("the original operation must join the held final probe");
            fixture.ViewModel.IsLocalModelSetupActive.Should().BeTrue();
            fixture.Audit.Events.Where(item =>
                string.Equals(item.ActionId, "local-model.install", StringComparison.Ordinal))
                .Should().ContainSingle().Which.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        }
        finally
        {
            fixture.Probe.Gate.TrySetResult();
            continuation.Release();
            await install;
        }

        fixture.ViewModel.IsLocalModelSetupActive.Should().BeFalse();
        fixture.ViewModel.IsLocalModelSetupProgressIndeterminate.Should().BeFalse();
        fixture.ViewModel.SetupTasks.Should().NotContain(task => task.State == SetupTaskState.Running);
        setupStates.Count(task => task.State != SetupTaskState.Running).Should().Be(1);
        fixture.LocalModel.Calls.Should().Be(1);
        fixture.PowerShell.InstallCalls.Should().Be(0);
        fixture.Reasoner.Requests.Should().BeEmpty();
        if (cancel)
        {
            fixture.ViewModel.ResponseTitle.Should().Be("Setup cancelled.");
            fixture.ViewModel.ResponseBody.Should().Be(
                "Local model installation was stopped; refresh readiness to check partial progress.");
            fixture.ViewModel.LocalModelSetupStatus.Should().Be("Local model setup was cancelled.");
            fixture.ViewModel.LocalModelSetupProgress.Should().NotBe(100);
            fixture.ViewModel.SetupTasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Cancelled);
            fixture.ViewModel.Dependencies.Should().ContainSingle()
                .Which.Readiness.Should().Be(DependencyReadiness.Missing);
            AssertAuditPair(fixture, SecurityAuditCategory.ResourceWrite, "local-model.install",
                SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Cancelled, "user-cancelled");

            fixture.Probe.Gate = null;
            fixture.Probe.Status = fixture.Probe.Status with { Readiness = DependencyReadiness.Ready };
            await fixture.ViewModel.RefreshCommand.ExecuteAsync();
            fixture.ViewModel.Dependencies.Should().ContainSingle()
                .Which.Readiness.Should().Be(DependencyReadiness.Ready);
            fixture.LocalModel.Calls.Should().Be(1);
            fixture.Reasoner.Requests.Should().BeEmpty();
        }
        else if (readiness == DependencyReadiness.Ready && !failProbe)
        {
            fixture.ViewModel.ResponseTitle.Should().Be("Local model is ready.");
            fixture.ViewModel.LocalModelSetupStatus.Should().Be("Final verification.");
            fixture.ViewModel.LocalModelSetupProgress.Should().Be(100);
            fixture.ViewModel.SetupTasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Completed);
            fixture.ViewModel.Dependencies.Should().ContainSingle()
                .Which.Readiness.Should().Be(DependencyReadiness.Ready);
            AssertAuditPair(fixture, SecurityAuditCategory.ResourceWrite, "local-model.install",
                SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded);
        }
        else
        {
            fixture.ViewModel.ResponseTitle.Should().Be("Local model setup failed.");
            fixture.ViewModel.ResponseBody.Should().Be(failProbe
                ? "Verification failed."
                : "Local inference did not pass the readiness check: Final verification.");
            fixture.ViewModel.LocalModelSetupProgress.Should().NotBe(100);
            fixture.ViewModel.SetupTasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Failed);
            fixture.ViewModel.Dependencies.Should().ContainSingle()
                .Which.Readiness.Should().Be(DependencyReadiness.Missing);
            AssertAuditPair(fixture, SecurityAuditCategory.ResourceWrite, "local-model.install",
                SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Failed, "setup-failed");
        }
    }

    // Hold the existing fake probe after its cancellable wait succeeds, before its normal return.
    private sealed class HeldProbeContinuation : SynchronizationContext
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> pending =
            Channel.CreateUnbounded<(SendOrPostCallback, object?)>();
        private readonly Lock gate = new();
        private bool released;

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (gate)
            {
                if (!released)
                {
                    pending.Writer.TryWrite((d, state));
                    return;
                }
            }
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }

        public async Task RunNextAsync(CancellationToken cancellationToken)
        {
            var (callback, state) = await pending.Reader.ReadAsync(cancellationToken);
            var original = Current;
            try
            {
                SetSynchronizationContext(this);
                callback(state);
            }
            finally
            {
                SetSynchronizationContext(original);
            }
        }

        public async Task WaitForPostAsync(CancellationToken cancellationToken) =>
            await pending.Reader.WaitToReadAsync(cancellationToken);

        public void Release()
        {
            lock (gate)
            {
                released = true;
                while (pending.Reader.TryRead(out var work))
                {
                    ThreadPool.QueueUserWorkItem(static item => item.Callback(item.State), work, preferLocal: false);
                }
            }
        }
    }
}
