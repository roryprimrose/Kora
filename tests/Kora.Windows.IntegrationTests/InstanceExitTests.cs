using AwesomeAssertions;

using Kora.Core.Coordination;
using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests;

public sealed class InstanceExitTests
{
    [Theory]
    [InlineData("user")]
    [InlineData("session")]
    [InlineData("build")]
    [InlineData("path")]
    [InlineData("epoch")]
    [InlineData("transaction")]
    [InlineData("ticket")]
    [InlineData("detail")]
    [InlineData("kind")]
    [InlineData("version")]
    public void ExitCannotTargetAnotherIdentityOrCarryLifecycleAuthority(string change)
    {
        var owner = Identity(10);
        var caller = Identity(20);
        var request = new InstanceMessage(InstanceProtocol.Version, "Exit");
        switch (change)
        {
            case "user": caller = caller with { UserSid = "another-fixture-user" }; break;
            case "session": caller = caller with { SessionId = 2 }; break;
            case "build": caller = caller with { Build = caller.Build with { ContentDigest = new string('b', 64) } }; break;
            case "path": caller = caller with { Build = caller.Build with { ExecutablePath = @"C:\another\Kora.exe" } }; break;
            case "epoch": request = request with { Epoch = Guid.NewGuid() }; break;
            case "transaction": request = request with { Transaction = Guid.NewGuid() }; break;
            case "ticket": request = request with { Ticket = new string('a', 64) }; break;
            case "detail": request = request with { Detail = "untrusted task data" }; break;
            case "kind": request = request with { Kind = "InstallerStop" }; break;
            case "version": request = request with { Version = InstanceProtocol.Version + 1 }; break;
        }
        var apply = () => InstanceExitPolicy.RequireRequest(request, owner, caller);
        if (change is "user" or "session" or "build" or "path")
        { apply.Should().Throw<InvalidOperationException>(); }
        else { apply.Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void ExitAdmitsOnlyTheSameCanonicalExecutableAndBuild()
    {
        var owner = Identity(10);
        var caller = Identity(20) with { Build = owner.Build with { ExecutablePath = @"c:\FIXTURE\kora.EXE" } };
        var apply = () => InstanceExitPolicy.RequireRequest(new(InstanceProtocol.Version, "Exit"), owner, caller);
        apply.Should().NotThrow();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 3)]
    public async Task CliWaitsForVerifiedProcessResultAndDoesNotHideFailureExitCode(int processExitCode, int expected)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var completion = new TaskCompletionSource<InstanceExitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = ExitCommandStartup.RunAsync(["--exit"], _ => completion.Task, output, error,
            TestContext.Current.CancellationToken);
        command.IsCompleted.Should().BeFalse();
        output.ToString().Should().BeEmpty();
        completion.SetResult(new(InstanceExitStatus.Exited, 42, processExitCode));
        (await command).Should().Be(expected);
        output.ToString().Should().Contain("EXITED: processId=42;");
        if (processExitCode != 0) { error.ToString().Should().Contain("clean shutdown is not confirmed"); }
    }

    [Theory]
    [InlineData("arguments", 2)]
    [InlineData("denied", 1)]
    [InlineData("timeout", 1)]
    [InlineData("invalid-result", 1)]
    public async Task CliReportsExplicitFailureWithoutStartingDesktopOrClaimingExit(string failure, int expected)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var calls = 0;
        Task<InstanceExitResult> Request(CancellationToken token)
        {
            calls++;
            return failure switch
            {
                "denied" => Task.FromException<InstanceExitResult>(new InvalidOperationException("Synthetic denial.")),
                "timeout" => Task.FromException<InstanceExitResult>(new OperationCanceledException(token)),
                _ => Task.FromResult(new InstanceExitResult(InstanceExitStatus.Exited)),
            };
        }
        (await ExitCommandStartup.RunAsync(failure is "arguments" ? ["--exit", "unexpected"] : ["--exit"],
            Request, output, error, TestContext.Current.CancellationToken)).Should().Be(expected);
        calls.Should().Be(failure is "arguments" ? 0 : 1);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task CliCancellationStillWritesFailureDiagnostics()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        (await ExitCommandStartup.RunAsync(["--exit"], token => Task.FromCanceled<InstanceExitResult>(token),
            output, error, cancellation.Token)).Should().Be(1);

        output.ToString().Should().BeEmpty();
        error.ToString().Should().Contain("Graceful exit was not confirmed")
            .And.Contain("No process was forcibly terminated");
    }

    [Fact]
    public async Task CliNoOwnerDoesNotClaimCleanShutdown()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        (await ExitCommandStartup.RunAsync(["--exit"], _ => Task.FromResult(new InstanceExitResult(InstanceExitStatus.NoOwner)),
            output, error, TestContext.Current.CancellationToken)).Should().Be(0);
        output.ToString().Should().Contain("NO_OWNER").And.Contain("clean shutdown is not implied");
        error.ToString().Should().BeEmpty();
    }

    private static InstanceProcessIdentity Identity(int pid) =>
        new("fixture-user", 1, pid, pid, new("1", "fixture", new string('a', 64), true,
            @"C:\fixture\Kora.exe", @"C:\fixture"));
}
