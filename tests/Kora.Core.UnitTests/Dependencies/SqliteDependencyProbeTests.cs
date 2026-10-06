using AwesomeAssertions;

using Kora.Core.Dependencies;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class SqliteDependencyProbeTests
{
    [Fact]
    public void RequireSchemaVersion_preserves_the_value_and_rejects_missing_results()
    {
        SqliteDependencyProbe.RequireSchemaVersion(1L).Should().Be(1L);

        var missing = () => SqliteDependencyProbe.RequireSchemaVersion(null);
        missing.Should().Throw<InvalidDataException>()
            .WithMessage("The database did not return a schema version.");
    }

    [Fact]
    public async Task ProbeAsync_creates_and_reopens_the_local_database()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kora-sqlite-test-{Guid.NewGuid():N}");
        try
        {
            var probe = new SqliteDependencyProbe(
                new TestPaths(root),
                NullLogger<SqliteDependencyProbe>.Instance);

            probe.TaskId.Should().Be("kora.sqlite");
            probe.TaskName.Should().Be("Local SQLite storage");
            var first = await probe.ProbeAsync(TestContext.Current.CancellationToken);
            var second = await probe.ProbeAsync(TestContext.Current.CancellationToken);

            first.Readiness.Should().Be(DependencyReadiness.Ready);
            second.Readiness.Should().Be(DependencyReadiness.Ready);
            first.Detail.Should().Contain("Durable content and authoritative evidence storage remain unavailable");
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(root, "kora.db"),
                    Pooling = false,
                }.ToString());
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(1L);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProbeAsync_does_not_replace_corrupt_existing_data()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kora-sqlite-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "kora.db");
        var original = new byte[4096];
        Array.Fill(original, (byte)0x7f);
        try
        {
            await File.WriteAllBytesAsync(path, original, TestContext.Current.CancellationToken);
            var probe = new SqliteDependencyProbe(
                new TestPaths(root),
                NullLogger<SqliteDependencyProbe>.Instance);

            var status = await probe.ProbeAsync(TestContext.Current.CancellationToken);

            status.Readiness.Should().Be(DependencyReadiness.Failed);
            (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken))
                .Should().Equal(original);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProbeAsync_does_not_migrate_a_newer_schema()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kora-sqlite-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "kora.db");
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 2";
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            var probe = new SqliteDependencyProbe(new TestPaths(root),
                NullLogger<SqliteDependencyProbe>.Instance);
            var result = await probe.ProbeAsync(TestContext.Current.CancellationToken);

            result.Readiness.Should().Be(DependencyReadiness.Incompatible);
            result.Detail.Should().Contain("version 2");
            await using var reopened = new SqliteConnection($"Data Source={path};Pooling=False");
            await reopened.OpenAsync(TestContext.Current.CancellationToken);
            await using var versionCommand = reopened.CreateCommand();
            versionCommand.CommandText = "PRAGMA user_version";
            (await versionCommand.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(2L);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProbeAsync_reports_integrity_failures_without_replacing_the_database()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kora-sqlite-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "kora.db");
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE example (id INTEGER PRIMARY KEY, value TEXT);
                    CREATE INDEX example_value ON example(value);
                    INSERT INTO example(value) VALUES ('keep this data');
                    PRAGMA writable_schema = ON;
                    DELETE FROM sqlite_schema WHERE name = 'example_value';
                    PRAGMA writable_schema = OFF;
                    """;
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            var probe = new SqliteDependencyProbe(new TestPaths(root),
                NullLogger<SqliteDependencyProbe>.Instance);
            var result = await probe.ProbeAsync(TestContext.Current.CancellationToken);

            result.Readiness.Should().Be(DependencyReadiness.Failed);
            result.Detail.Should().Contain("Database integrity check failed");
            await using var reopened = new SqliteConnection($"Data Source={path};Pooling=False");
            await reopened.OpenAsync(TestContext.Current.CancellationToken);
            await using var read = reopened.CreateCommand();
            read.CommandText = "SELECT value FROM example";
            (await read.ExecuteScalarAsync(TestContext.Current.CancellationToken))
                .Should().Be("keep this data");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record TestPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}
