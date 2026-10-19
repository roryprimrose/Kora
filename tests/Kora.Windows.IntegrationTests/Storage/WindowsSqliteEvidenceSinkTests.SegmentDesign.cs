using System.IO.MemoryMappedFiles;
using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteEvidenceSinkTests
{
    [WindowsFact]
    public void Sealed_database_and_journal_read_pins_allow_checked_reads_but_reject_mutation()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        using var host = HostActivity.BeginRoot(Request(), HostActivityLayer.Application, HostOperation.Request);
        var sink = new WindowsSqliteEvidenceSink(paths);
        sink.WriteDiagnostic(Diagnostic(host));
        var path = DatabasePath(paths);
        var before = File.ReadAllBytes(path);
        using var lease = sink.AcquireReadLease(TestContext.Current.CancellationToken);
        using var databasePin = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var journalPin = new FileStream(path + "-journal", FileMode.Open, FileAccess.Read, FileShare.Read);

        var overwrite = () => File.WriteAllBytes(path, [1, 2, 3]);
        var rename = () => File.Move(path, path + ".renamed");
        var deleteJournal = () => File.Delete(path + "-journal");
        overwrite.Should().Throw<IOException>();
        rename.Should().Throw<IOException>();
        deleteJournal.Should().Throw<IOException>();

        using var connection = sink.OpenReadOnly(TestContext.Current.CancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM application_log_events;";
        command.ExecuteScalar().Should().Be(1L);
        File.ReadAllBytes(path).Should().Equal(before);
    }

    [WindowsFact]
    public void Sealed_read_pin_is_refused_while_an_existing_write_handle_remains_open()
    {
        using var paths = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(paths);
        sink.Initialize();
        var path = DatabasePath(paths);
        using var existingWriter = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var pin = () =>
        {
            using var readPin = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        };

        pin.Should().Throw<IOException>();
    }

    [WindowsFact]
    public void Sealed_read_pin_is_refused_until_an_existing_writable_mapping_is_disposed()
    {
        using var paths = new OwnedStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(paths);
        sink.Initialize();
        var path = DatabasePath(paths);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        using var mapping = MemoryMappedFile.CreateFromFile(stream, null, 0,
            MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: true);
        using var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
        stream.Dispose();
        var pin = () =>
        {
            using var readPin = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        };

        pin.Should().Throw<IOException>();
        view.Dispose();
        mapping.Dispose();
        pin.Should().NotThrow();
    }

    [WindowsFact]
    public void Sqlite_page_cap_rejects_oversized_append_without_changing_committed_rows()
    {
        using var paths = new OwnedStorageFixture();
        var database = new RestrictedSqliteDatabase(paths, "SegmentDesign", "segment.db", 1263489587,
            ["CREATE TABLE sample(payload BLOB NOT NULL) STRICT"]);
        using var lease = database.AcquireLease(out var created);
        using var connection = database.Open(created);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA page_count;";
        var initialPages = (long)command.ExecuteScalar()!;
        command.CommandText = $"PRAGMA max_page_count={initialPages + 1};";
        command.ExecuteScalar().Should().Be(initialPages + 1);

        using (var transaction = connection.BeginTransaction())
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO sample VALUES(zeroblob(65536));";
            var append = () => insert.ExecuteNonQuery();
            append.Should().Throw<SqliteException>().Which.SqliteErrorCode.Should().Be(SQLitePCL.raw.SQLITE_FULL);
        }

        command.CommandText = "SELECT count(*) FROM sample;";
        command.ExecuteScalar().Should().Be(0L);
        command.CommandText = "PRAGMA page_count;";
        command.ExecuteScalar().Should().Be(initialPages);
        RestrictedSqliteDatabase.ValidateIntegrity(connection, TestContext.Current.CancellationToken);
        database.VerifyFiles();
    }
}
