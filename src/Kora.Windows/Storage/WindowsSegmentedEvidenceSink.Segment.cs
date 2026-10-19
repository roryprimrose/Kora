using System.Security.Cryptography;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSegmentedEvidenceSink
{
    private sealed class Segment(SegmentEntry entry, WindowsSqliteEvidenceSink sink,
        RestrictedStorageDirectory directory) : IDisposable
    {
        private FileStream? databasePin;
        private FileStream? journalPin;
        public SegmentEntry Entry { get; private set; } = entry;
        public WindowsSqliteEvidenceSink Sink => sink;
        private string DatabasePath => Path.Combine(directory.Root, "evidence.db");
        private string JournalPath => DatabasePath + "-journal";
        public long DatabaseLength => new FileInfo(DatabasePath).Length;

        public void AdmitAndSeal(bool initialAdmission = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (databasePin is not null) { throw new InvalidOperationException("The segment is already pinned."); }
            directory.Verify();
            directory.VerifyFile(DatabasePath);
            directory.VerifyFile(JournalPath);
            databasePin = new(DatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                journalPin = new(JournalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var state = sink.ReadSegmentState(initialAdmission, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var digest = Convert.ToHexString(sink.SegmentDigest());
                var journalDigest = Hash(journalPin);
                cancellationToken.ThrowIfCancellationRequested();
                var databaseIdentity = WindowsFileIdentity.Read(databasePin.SafeFileHandle);
                var journalIdentity = WindowsFileIdentity.Read(journalPin.SafeFileHandle);
                if (Entry.DatabaseDigest is not null && (!string.Equals(Entry.DatabaseDigest, digest, StringComparison.Ordinal)
                    || !string.Equals(Entry.JournalDigest, journalDigest, StringComparison.Ordinal)
                    || !string.Equals(Entry.DatabaseIdentity, databaseIdentity, StringComparison.Ordinal)
                    || !string.Equals(Entry.JournalIdentity, journalIdentity, StringComparison.Ordinal)
                    || Entry.AuditCount != state.AuditCount))
                {
                    throw new InvalidDataException("A retained evidence segment changed after it was sealed.");
                }
                Entry = Entry with
                {
                    Sealed = true, AuditCount = state.AuditCount, DatabaseDigest = digest,
                    JournalDigest = journalDigest, DatabaseIdentity = databaseIdentity, JournalIdentity = journalIdentity,
                };
                sink.ReleaseParsedHistory();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void VerifySeal()
        {
            directory.Verify();
            directory.VerifyFile(DatabasePath);
            directory.VerifyFile(JournalPath);
            if (databasePin is null || journalPin is null
                || !string.Equals(WindowsFileIdentity.Read(databasePin.SafeFileHandle), Entry.DatabaseIdentity, StringComparison.Ordinal)
                || !string.Equals(WindowsFileIdentity.Read(journalPin.SafeFileHandle), Entry.JournalIdentity, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A sealed evidence segment lost its admitted file protection.");
            }

            sink.RequireSegmentDigest(Entry.DatabaseDigest!);
            if (!string.Equals(Hash(journalPin), Entry.JournalDigest, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A sealed evidence journal changed after admission.");
            }
        }

        private static string Hash(FileStream stream)
        {
            stream.Position = 0;
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        public void UnsealForMaintenance()
        {
            VerifySeal();
            Dispose();
            Entry = Entry with
            {
                DatabaseDigest = null, JournalDigest = null, DatabaseIdentity = null, JournalIdentity = null,
            };
        }

        public void Dispose()
        {
            databasePin?.Dispose();
            databasePin = null;
            journalPin?.Dispose();
            journalPin = null;
        }
    }
}
