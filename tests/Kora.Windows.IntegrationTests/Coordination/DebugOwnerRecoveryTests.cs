#if DEBUG
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Windows.Coordination;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Coordination;

public sealed class DebugOwnerRecoveryTests
{
    [WindowsFact]
    public void Dead_idle_owner_recovers_without_replaying_work()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        previous.End(false);
        var calls = 0;
        var next = fixture.Next(owner =>
        {
            owner.Should().Be(fixture.Owner);
            calls++;
            return true;
        });
        next.CanStart.Should().BeTrue();
        next.Prepare();
        next.Recovered.Should().BeTrue();
        calls.Should().Be(2);
        File.Exists(fixture.Marker).Should().BeFalse();
        File.Exists(fixture.Record).Should().BeFalse();
    }

    [WindowsFact]
    public void Live_owner_cannot_be_recovered_even_when_no_work_is_recorded()
    {
        using var fixture = new Fixture();
        fixture.Begin().End(false);
        var next = fixture.Next(_ => false);
        next.CanStart.Should().BeFalse();
        var recover = next.Prepare;
        recover.Should().Throw<InvalidOperationException>().WithMessage("*no verified clean exit*");
        File.Exists(fixture.Marker).Should().BeTrue();
        File.Exists(fixture.Record).Should().BeTrue();
    }

    [WindowsFact]
    public void Outstanding_work_blocks_recovery_without_assuming_process_death_stopped_it()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        using var work = previous.BeginWork();
        previous.End(false);
        fixture.Next(_ => true).CanStart.Should().BeFalse();
    }

    [WindowsFact]
    public void Only_explicitly_completed_work_releases_its_durable_hold()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        using var first = previous.BeginWork();
        using var second = previous.BeginWork();
        first.Complete();
        fixture.Next(_ => true).CanStart.Should().BeFalse();
        second.Complete();
        previous.End(false);
        fixture.Next(_ => true).CanStart.Should().BeTrue();
        var duplicate = second.Complete;
        duplicate.Should().Throw<ObjectDisposedException>();
    }

    [WindowsFact]
    public void Abandoned_work_holds_further_external_admission_and_clean_exit()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        previous.BeginWork().Dispose();
        var admit = previous.BeginWork;
        admit.Should().Throw<InvalidOperationException>();
        var close = () => previous.End(true);
        close.Should().Throw<InvalidOperationException>().WithMessage("*remains uncertain*");
        fixture.Next(_ => true).CanStart.Should().BeFalse();
    }

    [WindowsFact]
    public void Verified_clean_exit_removes_the_Debug_record()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        using var work = previous.BeginWork();
        work.Complete();
        previous.End(true);
        File.Exists(fixture.Record).Should().BeFalse();
    }

    [WindowsFact]
    public void Legacy_dirty_bit_is_not_reinterpreted_as_safe()
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.Marker, [1]);
        var next = fixture.Next(_ => true);
        next.CanStart.Should().BeFalse();
        var recover = next.Prepare;
        recover.Should().Throw<InvalidOperationException>();
        File.ReadAllBytes(fixture.Marker).Should().Equal([1]);
    }

    [WindowsFact]
    public void Record_without_marker_still_preserves_external_work_uncertainty()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        using var work = previous.BeginWork();
        previous.End(false);
        File.Delete(fixture.Marker);
        fixture.Next(_ => true).CanStart.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(59)]
    [InlineData(61)]
    public void Invalid_record_lengths_fail_closed(int length)
    {
        using var fixture = new Fixture();
        fixture.Begin().End(false);
        File.WriteAllBytes(fixture.Record, new byte[length]);
        var recover = () => fixture.Next(_ => true).CanStart;
        recover.Should().Throw<InvalidDataException>();
        File.Exists(fixture.Marker).Should().BeTrue();
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(8, 0)]
    [InlineData(12, 0)]
    [InlineData(16, 0)]
    [InlineData(24, -1)]
    [InlineData(24, 65)]
    public void Unsupported_or_invalid_record_fields_fail_closed(int offset, int value)
    {
        using var fixture = new Fixture();
        fixture.Begin().End(false);
        var bytes = File.ReadAllBytes(fixture.Record);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        SHA256.HashData(bytes.AsSpan(0, 28), bytes.AsSpan(28));
        File.WriteAllBytes(fixture.Record, bytes);
        var recover = () => fixture.Next(_ => true).CanStart;
        recover.Should().Throw<InvalidDataException>();
    }

    [WindowsFact]
    public void Corrupted_work_count_cannot_become_a_clean_record()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        using var work = previous.BeginWork();
        previous.End(false);
        var bytes = File.ReadAllBytes(fixture.Record);
        bytes[24] = 0;
        File.WriteAllBytes(fixture.Record, bytes);
        var recover = () => fixture.Next(_ => true).CanStart;
        recover.Should().Throw<InvalidDataException>();
    }

    [WindowsFact]
    public void Unknown_owner_lifetime_is_an_error_not_a_dead_owner()
    {
        using var fixture = new Fixture();
        fixture.Begin().End(false);
        var next = fixture.Next(_ => throw new UnauthorizedAccessException("Cannot query owner."));
        var recover = next.Prepare;
        recover.Should().Throw<UnauthorizedAccessException>();
        File.Exists(fixture.Record).Should().BeTrue();
    }

    [WindowsFact]
    public void Failed_record_publication_prevents_admission_and_cleans_its_temporary_file()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        File.Delete(fixture.Record);
        Directory.CreateDirectory(fixture.Record);
        var admit = previous.BeginWork;
        admit.Should().Throw<UnauthorizedAccessException>();
        Directory.GetFiles(fixture.Directory, "*.tmp").Should().BeEmpty();
        var retry = previous.BeginWork;
        retry.Should().Throw<InvalidOperationException>();
        var close = () => previous.End(true);
        close.Should().Throw<InvalidOperationException>();
    }

    [WindowsFact]
    public void Work_bound_is_exact_and_does_not_lose_accepted_leases()
    {
        using var fixture = new Fixture();
        var previous = fixture.Begin();
        var leases = Enumerable.Range(0, 64).Select(_ => previous.BeginWork()).ToArray();
        var overflow = previous.BeginWork;
        overflow.Should().Throw<InvalidOperationException>();
        foreach (var lease in leases) { lease.Complete(); lease.Dispose(); }
        previous.End(false);
        fixture.Next(_ => true).CanStart.Should().BeTrue();
    }

    [WindowsFact]
    public void Native_lifetime_check_distinguishes_current_owner_from_reused_PID()
    {
        using var fixture = new Fixture();
        var current = DebugOwnerRecovery.CurrentOwner();
        fixture.Begin(current).End(false);
        fixture.Next().CanStart.Should().BeFalse();
        fixture.Begin(current with { CreationTime = current.CreationTime - 1 }).End(false);
        fixture.Next().CanStart.Should().BeTrue();
    }

    [WindowsFact]
    public void Abandoned_mutex_acquisition_still_requires_a_safe_recovery_record()
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.Marker, [1]);
        using var mutex = new Mutex(false, $@"Local\Kora.Tests.DebugRecovery.{Guid.NewGuid():N}");
        var thread = new Thread(() => mutex.WaitOne());
        thread.Start();
        thread.Join();
        DebugOwnerRecovery.AcquireForRecovery(mutex, TimeSpan.Zero).Should().BeTrue();
        try
        {
            var recover = fixture.Next(_ => true).Prepare;
            recover.Should().Throw<InvalidOperationException>();
        }
        finally { mutex.ReleaseMutex(); }
    }

    [WindowsFact]
    public async Task External_launchers_require_tracking_before_starting_any_process()
    {
        using var fixture = new Fixture();
        var inactive = fixture.Next();
        var ollama = new Kora.Windows.Dependencies.OllamaProcessRunner(inactive);
        var start = () => ollama.StartServer("not-an-executable");
        start.Should().Throw<InvalidOperationException>().WithMessage("*admission is unavailable*");
        var install = () => ollama.InstallWingetAsync(TestContext.Current.CancellationToken);
        await install.Should().ThrowAsync<InvalidOperationException>().WithMessage("*admission is unavailable*");
        var powershell = new Kora.Windows.Dependencies.PowerShellProcessRunner(inactive);
        var verify = () => powershell.GetVersionAsync("not-an-executable", TestContext.Current.CancellationToken);
        await verify.Should().ThrowAsync<InvalidOperationException>().WithMessage("*admission is unavailable*");
        File.Exists(fixture.Record).Should().BeFalse();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string sid;
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), $"Kora.DebugRecovery.{Guid.NewGuid():N}");
        internal string Marker => Path.Combine(Directory, "unclean-owner");
        internal string Record => Marker + ".debug-recovery";
        internal DebugRecoveryOwner Owner { get; } = new(1234, 1, 1234);

        internal Fixture()
        {
            if (!OperatingSystem.IsWindows()) { Assert.Skip("Requires Windows native APIs."); }
            using var identity = WindowsIdentity.GetCurrent();
            sid = identity.User!.Value;
            System.IO.Directory.CreateDirectory(Directory);
        }

        internal DebugOwnerRecovery Begin(DebugRecoveryOwner? owner = null)
        {
            File.WriteAllBytes(Marker, [1]);
            var recovery = new DebugOwnerRecovery(Marker, sid, owner ?? Owner, _ => false);
            recovery.Begin();
            return recovery;
        }

        internal DebugOwnerRecovery Next(Func<DebugRecoveryOwner, bool>? exited = null) =>
            new(Marker, sid, Owner, exited);

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
#endif
