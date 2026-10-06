using AwesomeAssertions;
using Kora.Setup;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerPreflightTests
{
    [Fact]
    public void VersionProbeSuppressesProfilesTelemetryAndUpdateChecks()
    {
        var start = PowerShellProcessRunner.CreateVersionStartInfo(@"C:\fixture\pwsh.exe");
        start.UseShellExecute.Should().BeFalse();
        start.ArgumentList.Should().Equal("-NoLogo", "-NoProfile", "-NonInteractive",
            "-Command", "$PSVersionTable.PSVersion.ToString()");
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"].Should().Be("1");
        start.Environment["POWERSHELL_UPDATECHECK"].Should().Be("Off");
    }

    [Theory]
    [InlineData(InstallerDependencyState.Checking, false)]
    [InlineData(InstallerDependencyState.Installed, false)]
    [InlineData(InstallerDependencyState.Missing, true)]
    [InlineData(InstallerDependencyState.UpdateRequired, true)]
    [InlineData(InstallerDependencyState.Detected, true)]
    [InlineData(InstallerDependencyState.NotRunning, true)]
    [InlineData(InstallerDependencyState.NeedsPreparation, true)]
    [InlineData(InstallerDependencyState.Incompatible, false)]
    [InlineData(InstallerDependencyState.Failed, false)]
    public void OnlyKnownActionableStatesOfferPreparation(InstallerDependencyState state, bool actionable)
    {
        new InstallerDependencyStatus(state, "Detail").CanPrepare.Should().Be(actionable);
    }

    [Theory]
    [InlineData(InstallerDependencyState.Installed, "Installed - will be reused")]
    [InlineData(InstallerDependencyState.Detected, "Detected - verification required")]
    [InlineData(InstallerDependencyState.Incompatible, "Incompatible - preparation blocked")]
    [InlineData(InstallerDependencyState.Failed, "Couldn't verify - preparation blocked")]
    public void SummaryPreservesReadinessAndBlockingWithoutDiagnosticDetail(
        InstallerDependencyState state, string expected)
    {
        var status = new InstallerDependencyStatus(state, @"Diagnostic path C:\fixture\component");
        status.Summary.Should().Be(expected);
        status.Summary.Should().NotContain("fixture");
        status.Display.Should().Contain(status.Detail);
    }

    [Fact]
    public async Task InstalledAndUnknownComponentsCannotBeSelectedButKoraOnlyIsAllowed()
    {
        var result = new InstallerPreflightResult(
            new(InstallerDependencyState.Installed, "PowerShell 7.5"),
            new(InstallerDependencyState.Incompatible, "Wrong model digest"),
            new(InstallerDependencyState.Failed, "Unreadable assets"), NoStartup(), NoStartup());
        var state = State(_ => Task.FromResult(result));
        await state.CheckAsync(TestContext.Current.CancellationToken);
        state.IsComplete.Should().BeTrue();
        state.Result.Should().Be(result);
        var powerShell = () => state.ValidateSelection(new OptionalComponents(PowerShell: true));
        var ollama = () => state.ValidateSelection(new OptionalComponents(LocalInference: true));
        var kokoro = () => state.ValidateSelection(new OptionalComponents(Kokoro: true));
        powerShell.Should().Throw<InvalidOperationException>();
        ollama.Should().Throw<InvalidOperationException>();
        kokoro.Should().Throw<InvalidOperationException>();
        state.ValidateSelection(new OptionalComponents());
    }

    [Fact]
    public async Task DetectionIsNotApprovalAndCheckingCannotAdmitPreparation()
    {
        var completion = new TaskCompletionSource<InstallerPreflightResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = State(_ => completion.Task);
        var check = state.CheckAsync(TestContext.Current.CancellationToken);
        state.IsChecking.Should().BeTrue();
        var approve = () => state.ValidateSelection(new OptionalComponents(PowerShell: true));
        approve.Should().Throw<InvalidOperationException>();
        var duplicate = () => state.CheckAsync(TestContext.Current.CancellationToken);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        completion.SetResult(Missing());
        await check;
        state.IsChecking.Should().BeFalse();
        state.ValidateSelection(new OptionalComponents(PowerShell: true));
    }

    [Fact]
    public async Task CancellationRejectsEvenALateSuccessfulProbe()
    {
        using var cancellation = new CancellationTokenSource();
        var completion = new TaskCompletionSource<InstallerPreflightResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = State(_ => completion.Task);
        var check = state.CheckAsync(cancellation.Token);
        cancellation.Cancel();
        completion.SetResult(Missing());
        var awaitCheck = () => check;
        await awaitCheck.Should().ThrowAsync<OperationCanceledException>();
        state.IsComplete.Should().BeFalse();
        state.IsChecking.Should().BeFalse();
        state.Result.Should().Be(InstallerPreflightResult.Checking);
    }

    [Fact]
    public async Task FailureIsReportedAsUnknownNotMissingAndRetryReplacesIt()
    {
        var fail = true;
        var state = State(_ => fail
            ? throw new IOException("Access failed.")
            : Task.FromResult(Missing()));
        await state.CheckAsync(TestContext.Current.CancellationToken);
        state.IsComplete.Should().BeTrue();
        state.Result.PowerShell.State.Should().Be(InstallerDependencyState.Failed);
        state.Result.PowerShell.Detail.Should().Contain("Access failed");
        fail = false;
        await state.CheckAsync(TestContext.Current.CancellationToken);
        state.Result.PowerShell.State.Should().Be(InstallerDependencyState.Missing);
    }

    [Theory]
    [InlineData(InstallerDependencyState.Checking)]
    [InlineData((InstallerDependencyState)99)]
    public async Task IncompleteOrUnknownResultsFailClosed(InstallerDependencyState unexpected)
    {
        var bad = new InstallerDependencyStatus(unexpected, "Not complete");
        var state = State(_ => Task.FromResult(new InstallerPreflightResult(bad, bad, bad, NoStartup(), NoStartup())));
        await state.CheckAsync(TestContext.Current.CancellationToken);
        state.Result.PowerShell.State.Should().Be(InstallerDependencyState.Failed);
        state.Result.PowerShell.CanPrepare.Should().BeFalse();
    }

    private static InstallerPreflightResult Missing()
    {
        var missing = new InstallerDependencyStatus(InstallerDependencyState.Missing, "Not installed");
        return new(missing, missing, missing, NoStartup(), NoStartup());
    }

    private static InstallerStartupStatus NoStartup() => new(InstallerStartupState.NotRegistered, "No startup entry.");

    private static InstallerPreflightState State(Func<CancellationToken, Task<InstallerPreflightResult>> probe)
        => new(new Probe(probe), NullLogger<InstallerPreflightState>.Instance);

    private sealed class Probe(Func<CancellationToken, Task<InstallerPreflightResult>> probe) : IInstallerPreflight
    {
        public Task<InstallerPreflightResult> ProbeAsync(CancellationToken cancellationToken) => probe(cancellationToken);
    }
}
