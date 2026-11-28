using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Kora.Core.Skills;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Skills;
using Kora.Windows.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed partial class OwnedSkillPackagePublicationTests
{
    private static readonly TimeSpan[] RetrySchedule =
        [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)];

    [Fact]
    public async Task TransientRenameRefusalRetriesOnlyTheSameNoReplaceRenameWithinTheBound()
    {
        using var paths = new OwnedStorageFixture();
        using var held = new HeldFile();
        var clock = new RetryClock(_ => held.Dispose());
        var logs = new RecordingLogger();
        var store = new WindowsOwnedDeclarativeSkillStore(paths, clock, logs)
        {
            Checkpoint = point => { if (point is "before-commit") { held.Hold(HoldStagedManifest(paths, FileShare.ReadWrite | FileShare.Delete)); } },
        };
        var saved = await Save(store, Document(), Token);
        saved.Document.RevisionDigest.Should().Be(Document().RevisionDigest);
        (await store.Read("example", "1.0.0", Token))!.ManifestIdentity.Should().Be(saved.ManifestIdentity);
        // The held child refuses the first rename; on-access scanning on a developer machine may refuse a later one too.
        clock.Delays.Should().NotBeEmpty().And.Equal(RetrySchedule.Take(clock.Delays.Length));
        var retries = logs.Messages(7840);
        retries.Should().HaveCount(clock.Delays.Length);
        retries[0].Should().Contain("(Windows error 5)").And.Contain("attempt 2 of 4").And.Contain("after 50 ms");
        logs.All.Should().NotContain(message => message.Contains(paths.LocalRoot, StringComparison.OrdinalIgnoreCase));
        Directory.EnumerateDirectories(Path.Combine(paths.RoamingRoot, "Skills", "example"), ".owned-skill-*").Should().BeEmpty();
    }

    [Fact]
    public async Task ExhaustedTransientRefusalFailsClosedWithTheUnchangedErrorAndRetrySaveRecovers()
    {
        using var paths = new OwnedStorageFixture();
        using var held = new HeldFile();
        var clock = new RetryClock();
        var logs = new RecordingLogger();
        var store = new WindowsOwnedDeclarativeSkillStore(paths, clock, logs)
        {
            Checkpoint = point => { if (point is "before-commit") { held.Hold(HoldStagedManifest(paths, FileShare.ReadWrite | FileShare.Delete)); } },
        };
        var run = () => Save(store, Document(), Token);
        (await run.Should().ThrowAsync<IOException>()).WithMessage("Atomic no-replace skill directory publication failed.")
            .WithInnerException<Win32Exception>().Which.NativeErrorCode.Should().Be(5);
        clock.Delays.Should().Equal(RetrySchedule);
        logs.Messages(7840).Select(message => message[message.IndexOf("attempt", StringComparison.Ordinal)..])
            .Should().Equal("attempt 2 of 4 follows after 50 ms. No content or path recorded.",
                "attempt 3 of 4 follows after 100 ms. No content or path recorded.",
                "attempt 4 of 4 follows after 200 ms. No content or path recorded.");
        Directory.Exists(store.Destination(Document())).Should().BeFalse();
        held.Dispose();
        store.Checkpoint = null;
        (await store.List(Token)).Should().BeEmpty();
        var saved = await Save(store, Document(), Token);
        saved.Document.RevisionDigest.Should().Be(Document().RevisionDigest);
        Directory.EnumerateDirectories(Path.Combine(paths.RoamingRoot, "Skills", "example"), ".owned-skill-*").Should().BeEmpty();
    }

    [Fact]
    public async Task DestinationCollisionIsFinalAndIsNeverRetriedOrReplaced()
    {
        using var paths = new OwnedStorageFixture();
        var clock = new RetryClock();
        var logs = new RecordingLogger();
        var directory = RestrictedStorageDirectory.ForOwnedSkills(paths);
        var store = new WindowsOwnedDeclarativeSkillStore(paths, clock, logs);
        var marker = Path.Combine(store.Destination(Document()), "external.txt");
        store.Checkpoint = point =>
        {
            if (point is not "before-commit") { return; }
            directory.CreateOwnedSkillDirectory(store.Destination(Document()));
            using var external = directory.CreateNewFile(marker);
            external.Write("external data must survive"u8);
        };
        var run = () => Save(store, Document(), Token);
        (await run.Should().ThrowAsync<InvalidOperationException>()).WithMessage("That exact skill version already exists*");
        File.ReadAllText(marker).Should().Be("external data must survive");
        // Only a transient refusal may be followed by a retry; the collision status itself is never retried.
        logs.Messages(7840).Should().HaveCount(clock.Delays.Length)
            .And.OnlyContain(message => message.Contains("(Windows error 5)") || message.Contains("(Windows error 32)"));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("revoke")]
    public async Task CancellationOrRevocationDuringTheRetryWaitStopsBeforeAnotherRename(string change)
    {
        using var paths = new OwnedStorageFixture();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var current = true;
        using var held = new HeldFile();
        var clock = new RetryClock(_ =>
        {
            // Releasing the holder first means any further rename attempt would succeed and publish.
            held.Dispose();
            if (change is "cancel") { cancel.Cancel(); } else { current = false; }
        });
        var store = new WindowsOwnedDeclarativeSkillStore(paths, clock, new RecordingLogger())
        {
            Checkpoint = point => { if (point is "before-commit") { held.Hold(HoldStagedManifest(paths, FileShare.ReadWrite | FileShare.Delete)); } },
        };
        var run = () => Save(store, Document(), cancel.Token, () => current);
        if (change is "cancel") { await run.Should().ThrowAsync<OperationCanceledException>(); }
        else { (await run.Should().ThrowAsync<InvalidOperationException>()).WithMessage("Original skill authoring authority expired."); }
        clock.Delays.Should().Equal(RetrySchedule[0]);
        Directory.Exists(store.Destination(Document())).Should().BeFalse();
        (await store.List(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task RenameRetryRechecksTheUnpinnedOriginalBeforeAnotherRename()
    {
        using var paths = new OwnedStorageFixture();
        using var held = new HeldFile();
        var originalInstructions = Path.Combine(paths.RoamingRoot, "Skills", "example", "1.0.0", "instructions.md");
        var clock = new RetryClock(_ =>
        {
            // The holder is released, so only the original-source recheck can stop the next rename.
            held.Dispose();
            File.AppendAllText(originalInstructions, "external");
        });
        var store = new WindowsOwnedDeclarativeSkillStore(paths, clock, new RecordingLogger());
        var original = await Save(store, Document(), Token);
        var proposal = new OwnedSkillRename(original, "renamed");
        store.Checkpoint = point =>
        {
            if (point is "before-commit") { held.Hold(HoldStagedManifest(paths, FileShare.ReadWrite | FileShare.Delete, "renamed")); }
        };
        var run = () => store.Rename(proposal, false, static () => true, operation => Task.FromResult(operation()), Token);
        (await run.Should().ThrowAsync<OwnedSkillRenameUnconfirmedException>())
            .WithInnerException<InvalidOperationException>().WithMessage("The original package identity or complete bytes changed*");
        clock.Delays.Should().Equal(RetrySchedule[0]);
        Directory.Exists(store.Destination(proposal.Renamed)).Should().BeFalse("no rename follows a changed original");
    }

    [Fact]
    public async Task CleanupFailureKeepsTheOriginalErrorAndALaterSaveReapsTheVerifiedOrphan()
    {
        using var paths = new OwnedStorageFixture();
        var logs = new RecordingLogger();
        using var held = new HeldFile();
        var store = new WindowsOwnedDeclarativeSkillStore(paths, new RetryClock(), logs)
        {
            Checkpoint = point =>
            {
                if (point is not "before-commit") { return; }
                // Sharing without delete makes the operation's own cleanup fail after the original fault.
                held.Hold(HoldStagedManifest(paths, FileShare.ReadWrite));
                throw new IOException("injected publication fault");
            },
        };
        var run = () => Save(store, Document(), Token);
        (await run.Should().ThrowAsync<IOException>()).WithMessage("injected publication fault");
        logs.Messages(7841).Should().ContainSingle().Which.Should().Contain("(System.IO.IOException)");
        var orphan = Directory.GetDirectories(Path.Combine(paths.RoamingRoot, "Skills", "example"), ".owned-skill-*").Should().ContainSingle().Which;
        held.Dispose();
        store.Checkpoint = null;
        var saved = await Save(store, Document(), Token);
        saved.Document.RevisionDigest.Should().Be(Document().RevisionDigest);
        Directory.Exists(orphan).Should().BeFalse();
        logs.Messages(7842).Should().ContainSingle().Which.Should().Contain("(3 verified entries)");
    }

    [Fact]
    public async Task VerifiedOrphanStagesOfTheSavedIdAreReapedWithoutTouchingPublishedOrOtherEntries()
    {
        using var paths = new OwnedStorageFixture();
        var logs = new RecordingLogger();
        var store = new WindowsOwnedDeclarativeSkillStore(paths, new RetryClock(), logs);
        var first = await Save(store, Document(), Token);
        await Save(store, OwnedDeclarativeSkillDocument.Create("another", "1.0.0", "Another", "Description", "other"), Token);
        var directory = RestrictedStorageDirectory.ForOwnedSkills(paths);
        var empty = Path.Combine(directory.Root, "example", StageName());
        directory.CreateOwnedSkillDirectory(empty);
        var populated = Path.Combine(directory.Root, "example", StageName());
        directory.CreateOwnedSkillDirectory(populated);
        WriteOwned(directory, Path.Combine(populated, "manifest.yaml"));
        directory.CreateOwnedSkillDirectory(Path.Combine(populated, "references"));
        WriteOwned(directory, Path.Combine(populated, "references", "a.md"));
        var otherId = Path.Combine(directory.Root, "another", StageName());
        directory.CreateOwnedSkillDirectory(otherId);
        await Save(store, Document("1.0.1"), Token);
        Directory.Exists(empty).Should().BeFalse();
        Directory.Exists(populated).Should().BeFalse();
        Directory.Exists(otherId).Should().BeTrue("reaping is limited to the exact ID being saved under its lease");
        (await store.Read("example", "1.0.0", Token))!.ManifestIdentity.Should().Be(first.ManifestIdentity);
        logs.Messages(7842).Should().HaveCount(2);
        logs.Messages(7843).Should().BeEmpty();
    }

    [Theory]
    [InlineData("junction-child", true)]
    [InlineData("hardlink-child", true)]
    [InlineData("uppercase-name", true)]
    [InlineData("unowned-acl", false)]
    [InlineData("junction-stage", false)]
    public async Task UnprovenOrphanStagesAreRetainedAndNeverFollowed(string shape, bool publishes)
    {
        using var paths = new OwnedStorageFixture();
        var logs = new RecordingLogger();
        var store = new WindowsOwnedDeclarativeSkillStore(paths, new RetryClock(), logs);
        await Save(store, Document(), Token);
        var directory = RestrictedStorageDirectory.ForOwnedSkills(paths);
        var external = Path.Combine(paths.LocalRoot, "external");
        Directory.CreateDirectory(external);
        var externalFile = Path.Combine(external, "external.txt");
        File.WriteAllText(externalFile, "external data must survive");
        var stage = Path.Combine(directory.Root, "example",
            shape is "uppercase-name" ? ".owned-skill-" + Guid.NewGuid().ToString("N").ToUpperInvariant() : StageName());
        string? retained = null;
        switch (shape)
        {
            case "junction-child":
                directory.CreateOwnedSkillDirectory(stage);
                WriteOwned(directory, Path.Combine(stage, "manifest.yaml"));
                retained = Path.Combine(stage, "manifest.yaml");
                await Junction(Path.Combine(stage, "link"), external);
                break;
            case "hardlink-child":
                directory.CreateOwnedSkillDirectory(stage);
                retained = Path.Combine(stage, "manifest.yaml");
                CreateHardLink(retained, externalFile, nint.Zero).Should().BeTrue();
                break;
            case "uppercase-name":
                directory.CreateOwnedSkillDirectory(stage);
                WriteOwned(directory, Path.Combine(stage, "manifest.yaml"));
                retained = Path.Combine(stage, "manifest.yaml");
                break;
            case "unowned-acl":
                Directory.CreateDirectory(stage);
                retained = Path.Combine(stage, "manifest.yaml");
                File.WriteAllText(retained, "not proven Kora staging");
                break;
            case "junction-stage":
                await Junction(stage, external);
                break;
        }
        var run = () => Save(store, Document("1.0.1"), Token);
        if (publishes) { await run(); }
        else
        {
            var thrown = (await run.Should().ThrowAsync<Exception>()).Which;
            (thrown is InvalidDataException or UnauthorizedAccessException).Should().BeTrue(thrown.ToString());
        }
        Directory.Exists(store.Destination(Document("1.0.1"))).Should().Be(publishes);
        Directory.Exists(stage).Should().BeTrue();
        if (retained is not null) { File.Exists(retained).Should().BeTrue(); }
        File.ReadAllText(externalFile).Should().Be("external data must survive");
        logs.Messages(7842).Should().BeEmpty();
        logs.Messages(7843).Should().HaveCount(shape is "uppercase-name" ? 0 : 1, "a noncanonical name is never treated as Kora staging");
        if (shape is "junction-child") { Directory.Delete(Path.Combine(stage, "link")); }
        if (shape is "junction-stage") { Directory.Delete(stage); }
    }

    private static string StageName() => ".owned-skill-" + Guid.NewGuid().ToString("N");

    private static void WriteOwned(RestrictedStorageDirectory directory, string path)
    {
        using var stream = directory.CreateNewFile(path);
        stream.Write("orphaned staged bytes"u8);
    }

    private static FileStream HoldStagedManifest(OwnedStorageFixture paths, FileShare share, string id = "example")
    {
        var stage = Directory.GetDirectories(Path.Combine(paths.RoamingRoot, "Skills", id), ".owned-skill-*").Single();
        return new FileStream(Path.Combine(stage, "manifest.yaml"), FileMode.Open, FileAccess.Read, share);
    }

    private static async Task Junction(string alias, string target)
    {
        // A junction needs no developer-mode or symlink privilege.
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c mklink /J \"" + alias + "\" \"" + target + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        })!;
        await process.WaitForExitAsync(Token);
        process.ExitCode.Should().Be(0);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string link, string existing, nint security);

    /// <summary>Owns the test's competing handle so a failed assertion still releases it before fixture cleanup.</summary>
    private sealed class HeldFile : IDisposable
    {
        private FileStream? stream;
        internal void Hold(FileStream value) => stream = value;
        public void Dispose() => Interlocked.Exchange(ref stream, null)?.Dispose();
    }

    /// <summary>Records each requested wait and elapses it at once, so retry timing never depends on the wall clock.</summary>
    private sealed class RetryClock(Action<TimeSpan>? onDelay = null) : TimeProvider
    {
        private readonly ConcurrentQueue<TimeSpan> delays = new();
        internal TimeSpan[] Delays => delays.ToArray();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            delays.Enqueue(dueTime);
            onDelay?.Invoke(dueTime);
            ThreadPool.UnsafeQueueUserWorkItem(_ => callback(state), null);
            return new ElapsedTimer();
        }

        private sealed class ElapsedTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<WindowsOwnedDeclarativeSkillStore>
    {
        private readonly ConcurrentQueue<(int Id, string Message)> entries = new();
        internal string[] All => entries.Select(entry => entry.Message).ToArray();
        internal string[] Messages(int eventId) => entries.Where(entry => entry.Id == eventId).Select(entry => entry.Message).ToArray();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Enqueue((eventId.Id, formatter(state, exception)));
    }
}
