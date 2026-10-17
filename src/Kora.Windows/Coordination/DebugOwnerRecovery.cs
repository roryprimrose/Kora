#if DEBUG
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;

namespace Kora.Windows.Coordination;

internal sealed class DebugOwnerRecovery : IDebugRecoveryWorkTracker
{
    private const int RecordLength = 60;
    private const int PayloadLength = 28;
    private const int MaximumWork = 64;
    private readonly string markerPath;
    private readonly string recordPath;
    private readonly string sid;
    private readonly DebugRecoveryOwner current;
    private readonly Func<DebugRecoveryOwner, bool> hasExited;
    private readonly Lock sync = new();
    private int outstanding;
    private bool active;
    private bool failed;
    internal bool Recovered { get; private set; }

    internal DebugOwnerRecovery(string markerPath, string sid, DebugRecoveryOwner current,
        Func<DebugRecoveryOwner, bool>? hasExited = null)
    {
        this.markerPath = markerPath;
        recordPath = markerPath + ".debug-recovery";
        this.sid = sid;
        this.current = current;
        this.hasExited = hasExited ?? HasExited;
    }

    internal static DebugRecoveryOwner CurrentOwner()
    {
        using var process = Process.GetCurrentProcess();
        if (!CoordinationNative.GetProcessTimes(process.SafeHandle, out var creation, out _, out _, out _))
        {
            throw new Win32Exception();
        }
        return new(process.Id, process.SessionId, creation);
    }

    internal bool CanStart
    {
        get
        {
            if (!File.Exists(recordPath))
            {
                return !File.Exists(markerPath);
            }
            var (previous, work) = Read();
            return work == 0 && hasExited(previous);
        }
    }

    internal void Prepare()
    {
        if (!CanStart)
        {
            throw new InvalidOperationException(
                $"The previous assistant has no verified clean exit. Debug automatic recovery cannot prove " +
                $"that the owner and external work have stopped. Reconcile the work, then explicitly remove " +
                $"'{markerPath}' and '{recordPath}' before launching manually.");
        }
        var recovering = File.Exists(markerPath) || File.Exists(recordPath);
        File.Delete(markerPath);
        File.Delete(recordPath);
        Recovered = recovering;
    }

    internal void Begin()
    {
        lock (sync)
        {
            Save();
            active = true;
        }
    }

    public IDebugRecoveryWork BeginWork()
    {
        lock (sync)
        {
            if (!active || failed || outstanding >= MaximumWork)
            {
                throw new InvalidOperationException("Debug external-work recovery admission is unavailable.");
            }
            outstanding++;
            Save();
            return new Work(this);
        }
    }

    private void CompleteWork()
    {
        lock (sync)
        {
            if (!active || failed || outstanding == 0)
            {
                throw new InvalidOperationException("Debug external-work completion cannot be verified.");
            }
            outstanding--;
            Save();
        }
    }

    private void HoldUncertain()
    {
        lock (sync) { failed = true; }
    }

    internal void End(bool safelyDisposed)
    {
        lock (sync)
        {
            active = false;
            if (!safelyDisposed) { return; }
            if (failed || outstanding != 0)
            {
                throw new InvalidOperationException(
                    "Debug external work remains uncertain; clean ownership release cannot be verified.");
            }
            File.Delete(recordPath);
        }
    }

    private (DebugRecoveryOwner Owner, int Work) Read()
    {
        using var file = new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        CoordinationNative.VerifyMutexDescriptor(
            file.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access),
            $"D:P(A;;FA;;;{sid})");
        if (file.Length != RecordLength)
        {
            throw new InvalidDataException("The Debug ownership recovery record has an invalid length.");
        }
        Span<byte> bytes = stackalloc byte[RecordLength];
        file.ReadExactly(bytes);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(bytes[..PayloadLength], digest);
        var owner = new DebugRecoveryOwner(BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]), BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]));
        var work = BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]);
        if (BinaryPrimitives.ReadInt32LittleEndian(bytes) != 0x4B4F5244
            || BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]) != 1
            || !CryptographicOperations.FixedTimeEquals(digest, bytes[PayloadLength..])
            || owner.ProcessId <= 0 || owner.SessionId <= 0 || owner.CreationTime <= 0
            || work is < 0 or > MaximumWork)
        {
            throw new InvalidDataException("The Debug ownership recovery record is malformed or unsupported.");
        }
        return (owner, work);
    }

    private void Save()
    {
        Span<byte> bytes = stackalloc byte[RecordLength];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 0x4B4F5244);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[8..], current.ProcessId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[12..], current.SessionId);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[16..], current.CreationTime);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[24..], outstanding);
        SHA256.HashData(bytes[..PayloadLength], bytes[PayloadLength..]);
        var temporary = recordPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = CoordinationNative.CreateUserContinuityFile(temporary, sid))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, recordPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            failed = true;
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    private bool HasExited(DebugRecoveryOwner previous)
    {
        using var process = CoordinationNative.OpenProcess(
            CoordinationNative.QueryLimitedInformation | CoordinationNative.Synchronize, false, previous.ProcessId);
        if (process.IsInvalid)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            if (error == 87) { return true; } // Windows reports ERROR_INVALID_PARAMETER for a departed PID.
            throw new Win32Exception(error);
        }
        if (!CoordinationNative.GetProcessTimes(process, out var creation, out _, out _, out _))
        {
            throw new Win32Exception();
        }
        if (creation != previous.CreationTime) { return true; }
        var identity = CoordinationNative.ReadProcessTokenIdentity(process);
        if (!string.Equals(identity.Sid, sid, StringComparison.Ordinal) || identity.SessionId != previous.SessionId)
        {
            throw new InvalidDataException("The previous Debug owner process identity cannot be verified.");
        }
        return CoordinationNative.WaitForSingleObject(process, 0) switch
        {
            0 => true,
            258 => false,
            _ => throw new Win32Exception(),
        };
    }

    internal static bool AcquireForRecovery(Mutex mutex, TimeSpan timeout)
    {
        try { return mutex.WaitOne(timeout); }
        catch (AbandonedMutexException)
        {
            // Acquisition alone grants no admission: the durable record must still prove safe recovery.
            return true;
        }
    }

    private sealed class Work(DebugOwnerRecovery owner) : IDebugRecoveryWork
    {
        private readonly Lock sync = new();
        private bool ended;

        public void Complete()
        {
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(ended, this);
                owner.CompleteWork();
                ended = true;
            }
        }

        // Disposing an incomplete lease deliberately retains durable uncertainty.
        public void Dispose()
        {
            lock (sync)
            {
                if (!ended) { owner.HoldUncertain(); }
                ended = true;
            }
        }
    }
}

#endif
