using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests.Coordination;

public sealed class InstanceProtocolTests
{
    [Fact]
    public async Task Versioned_bounded_message_round_trips_without_launch_or_task_arguments()
    {
        using var stream = new MemoryStream();
        var expected = new InstanceMessage(InstanceProtocol.Version, "Commit",
            Guid.NewGuid(), Guid.NewGuid(), new string('A', 64));
        await InstanceProtocol.WriteAsync(stream, expected, TestContext.Current.CancellationToken);
        stream.Position = 0;
        var actual = await InstanceProtocol.ReadAsync(stream, TestContext.Current.CancellationToken);
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4097)]
    [InlineData(int.MaxValue)]
    public async Task Untrusted_frame_lengths_are_rejected_before_allocation(int length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);
        var action = () => InstanceProtocol.ReadAsync(stream, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Incompatible_version_is_explicitly_rejected()
    {
        using var stream = new MemoryStream();
        await InstanceProtocol.WriteAsync(stream, new InstanceMessage(999, "Start"), TestContext.Current.CancellationToken);
        stream.Position = 0;
        var action = () => InstanceProtocol.ReadAsync(stream, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Truncated_message_is_not_an_acknowledgement()
    {
        using var stream = new MemoryStream();
        await InstanceProtocol.WriteAsync(stream, new InstanceMessage(InstanceProtocol.Version, "Activated"),
            TestContext.Current.CancellationToken);
        stream.SetLength(stream.Length - 1);
        stream.Position = 0;
        var action = () => InstanceProtocol.ReadAsync(stream, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<EndOfStreamException>();
    }

    [Fact]
    public async Task Null_untrusted_fields_are_explicitly_rejected()
    {
        using var stream = new MemoryStream();
        await InstanceProtocol.WriteAsync(stream,
            new InstanceMessage(InstanceProtocol.Version, null!), TestContext.Current.CancellationToken);
        stream.Position = 0;
        var action = () => InstanceProtocol.ReadAsync(stream, TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public void Process_handle_preserves_creation_user_session_and_lifetime_without_spawning_process()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        using var current = Process.GetCurrentProcess();
        using var handle = CoordinationNative.OpenProcess(
            CoordinationNative.Synchronize | CoordinationNative.QueryLimitedInformation, false, Environment.ProcessId);
        handle.IsInvalid.Should().BeFalse();
        var token = CoordinationNative.ReadProcessToken(handle);
        token.Sid.Should().Be(identity.User!.Value);
        token.SessionId.Should().Be(current.SessionId);
        CoordinationNative.GetProcessTimes(handle, out var creation, out _, out _, out _).Should().BeTrue();
        creation.Should().BeGreaterThan(0);
        CoordinationNative.WaitForSingleObject(handle, 0).Should().Be(258);
    }

    [Fact]
    public void Unknown_test_executable_is_denied_even_when_user_and_session_are_valid()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        using var current = Process.GetCurrentProcess();
        var action = () => VerifiedInstanceProcess.Open(
            Environment.ProcessId, identity.User!.Value, current.SessionId);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Restricted_user_mutex_excludes_a_second_thread_without_using_live_coordinator_names()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var name = $@"Local\Kora.Tests.{Guid.NewGuid():N}.{sid}";
        using var owner = CoordinationNative.CreateUserMutex(name, sid);
        owner.WaitOne(0).Should().BeTrue();
        try
        {
            var excluded = false;
            var thread = new Thread(() =>
            {
                using var candidate = CoordinationNative.CreateUserMutex(name, sid);
                excluded = !candidate.WaitOne(0);
                if (!excluded)
                {
                    candidate.ReleaseMutex();
                }
            });
            thread.Start();
            thread.Join();
            excluded.Should().BeTrue();
        }
        finally
        {
            owner.ReleaseMutex();
        }
    }

    [Fact]
    public async Task Current_user_only_pipe_authenticates_os_peer_pid_without_starting_assistant()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var name = $"Kora.Tests.{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var accept = server.WaitForConnectionAsync(timeout.Token);
        await client.ConnectAsync(timeout.Token);
        await accept;
        CoordinationNative.GetNamedPipeClientProcessId(server.SafePipeHandle, out var clientPid).Should().BeTrue();
        CoordinationNative.GetNamedPipeServerProcessId(client.SafePipeHandle, out var serverPid).Should().BeTrue();
        clientPid.Should().Be(Environment.ProcessId);
        serverPid.Should().Be(Environment.ProcessId);
    }
}
