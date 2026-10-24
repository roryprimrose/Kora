using AwesomeAssertions;

using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ExplicitNativeProposalIsSeparateFromFuturePolicyAndRetiresOnEditRefreshClose()
    {
        var unavailable = new Fixture();
        unavailable.ViewModel.SessionRetentionPlanStatus.Should().Contain("unavailable");
        unavailable.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        unavailable.ViewModel.ProposedSessionArchiveDays = 1;
        unavailable.ViewModel.ProposedSessionDeleteDays = null;
        unavailable.ViewModel.Dispose();
        var fixture = new Fixture(enableSessionRetention: true, enableDiagnosticRetention: true, enableSessionRetentionPlanner: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
        fixture.ViewModel.ProposedSessionArchiveDays.Should().BeNull();
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.ProposedSessionArchiveDays = 2;
        fixture.ViewModel.ProposedSessionArchiveDays = 2;
        fixture.ViewModel.ProposedSessionDeleteDays = 2;
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        fixture.ViewModel.ProposedSessionDeleteDays = 3;
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeTrue();
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("Complete")
            .And.Contain("archive 0; delete 1")
            .And.Contain("existing dates").And.Contain("unknown work status")
            .And.Contain("Current limits").And.NotContain("revision")
            .And.NotContain("metadata snapshot");
        fixture.SessionRetentionPreferences.Value.Should().BeNull();
        fixture.SessionRetentionConfiguration!.Settings.Should().Be(SessionRetentionSettings.Default);
        fixture.ViewModel.SelectedSessionArchiveDays.Should().Be(1);
        fixture.ViewModel.SelectedSessionDeleteDays.Should().Be(30);
        fixture.ViewModel.ProposedSessionDeleteDays = 4;
        fixture.ViewModel.ProposedSessionDeleteDays = 4;
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("edited");
        fixture.SessionPlanReader.Result = new(SessionRetentionPlanStatus.Incomplete, null, "stamp");
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("Incomplete").And.Contain("No count is available");
        fixture.SessionPlanReader.Result = new(SessionRetentionPlanStatus.Unavailable, null, null);
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("Unavailable").And.Contain("No count is available");
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("retired");
        fixture.SessionPlanReader.Result = new(SessionRetentionPlanStatus.Complete, new(0, 0, 0, 0, 0, 0, 0, 0, 0), "stamp");
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("Sessions checked: 0");
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => false);
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("retired");
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        fixture.ViewModel.Dispose();
        fixture.SessionPlanReader.Signal();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("source")]
    public async Task PrivacyOwnershipCallAndSourceLossRetireNativeResultsWithoutReplay(string reason)
    {
        var fixture = new Fixture(enableSessionRetention: true, enableDiagnosticRetention: true, enableSessionRetentionPlanner: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.ProposedSessionArchiveDays = 1;
        fixture.ViewModel.ProposedSessionDeleteDays = 3;
        await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        if (reason is "private")
        {
            var current = fixture.PrivacyObservation.Current;
            fixture.PrivacyObservation.Publish(new(current, current with { TopologyRevision = current.TopologyRevision + 1 },
                Kora.Core.Platform.WindowsPrivacyChangeReason.DeviceTopology));
        }
        if (reason is "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
        if (reason is "call")
        {
            await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
                TestContext.Current.CancellationToken);
        }
        if (reason is "source") { fixture.SessionPlanReader.Signal(); }
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("retired");
        fixture.SessionPlanReader.Requests.Should().HaveCount(1);
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("close")]
    [InlineData("private-revision")]
    [InlineData("owner-revision")]
    public async Task PendingNativePreviewCannotPublishAfterFailureClosureOrRevisionLoss(string reason)
    {
        var fixture = new Fixture(enableSessionRetention: true, enableDiagnosticRetention: true, enableSessionRetentionPlanner: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.ProposedSessionArchiveDays = 1;
        fixture.ViewModel.ProposedSessionDeleteDays = 3;
        var owner = 1L;
        fixture.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        var pending = new TaskCompletionSource<SessionRetentionPlanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SessionPlanReader.Pending = pending.Task;
        var task = fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync();
        if (reason is "close")
        {
            fixture.Dispatcher.DeferPosts = true;
            fixture.SessionPlanReader.Signal();
            fixture.ViewModel.Dispose();
            fixture.Dispatcher.FlushPosts();
        }
        if (reason is "private-revision")
        {
            var current = fixture.PrivacyObservation.Current;
            fixture.PrivacyObservation.Publish(new(current, current with { TopologyRevision = current.TopologyRevision + 1 },
                Kora.Core.Platform.WindowsPrivacyChangeReason.DeviceTopology));
        }
        if (reason is "owner-revision")
        {
            owner++;
            typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("RetireIneligibleSessionRetentionPlan",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(fixture.ViewModel, []);
        }
        if (reason is "failure") { pending.SetException(new IOException("private source unavailable")); }
        else { pending.SetResult(fixture.SessionPlanReader.Result); }
        await task;
        fixture.ViewModel.SessionRetentionPlanStatus.Should().NotContain("Sessions observed:");
        if (reason is "failure") { fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("failed"); }
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task DirectStaleCallbackIsRefusedEvenWhenInvokedAfterCommandEligibility()
    {
        var fixture = new Fixture();
        var method = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod("PreviewSessionRetentionAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(fixture.ViewModel, [])!;
        fixture.ViewModel.CanPreviewSessionRetention.Should().BeFalse();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PreviewFailureLoggingNeverRetainsPrivateExceptionMessagesOrPaths(bool enabled, bool unexpected)
    {
        var log = new RetentionPreviewLogger(enabled);
        var fixture = new Fixture(logger: log, enableSessionRetention: true, enableDiagnosticRetention: true,
            enableSessionRetentionPlanner: true);
        await using var admission = fixture.DiagnosticAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
        fixture.ViewModel.BindSessionRetentionNativeLifetime(static () => true);
        await fixture.ViewModel.RefreshSessionRetentionCommand.ExecuteAsync();
        fixture.ViewModel.ProposedSessionArchiveDays = 1;
        fixture.ViewModel.ProposedSessionDeleteDays = 3;
        fixture.SessionPlanReader.Pending = Task.FromException<SessionRetentionPlanResult>(
            unexpected ? new NotSupportedException(@"private-sentinel Q:\private\session-retention.txt")
                : new IOException(@"private-sentinel Q:\private\session-retention.txt"));
        if (unexpected)
        {
            fixture.ViewModel.PreviewSessionRetentionCommand.Execute(null);
            await log.Failure.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        else { await fixture.ViewModel.PreviewSessionRetentionCommand.ExecuteAsync(); }
        fixture.ViewModel.SessionRetentionPlanStatus.Should().Contain("failed").And.NotContain("private-sentinel");
        var failures = log.Events.Where(entry => entry.Name is "SessionRetentionPreviewFailed").ToArray();
        if (enabled)
        {
            failures.Should().ContainSingle();
            failures[0].Message.Should().Contain(unexpected ? nameof(NotSupportedException) : nameof(IOException))
                .And.NotContain("private-sentinel").And.NotContain(@"Q:\private");
            failures[0].Exception.Should().BeNull();
        }
        else { failures.Should().BeEmpty(); }
        fixture.ViewModel.Dispose();
    }

    private sealed class RetentionPreviewLogger(bool enabled) : ILogger<Kora.Application.ViewModels.MainViewModel>
    {
        internal List<(string? Name, string Message, Exception? Exception)> Events { get; } = [];
        internal TaskCompletionSource Failure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Events.Add((eventId.Name, formatter(state, exception), exception));
            if (eventId.Name is "SessionRetentionPreviewFailed") { Failure.TrySetResult(); }
        }
    }
}
