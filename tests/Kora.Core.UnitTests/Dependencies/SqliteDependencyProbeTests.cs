using AwesomeAssertions;

using Kora.Core.Dependencies;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class SqliteDependencyProbeTests
{
    [Fact]
    public async Task ProbeAsync_creates_and_reopens_the_local_database()
    {
        var root = Path.Combine(Path.GetTempPath(), $"kora-sqlite-test-{Guid.NewGuid():N}");
        try
        {
            var probe = new SqliteDependencyProbe(
                new TestPaths(root),
                NullLogger<SqliteDependencyProbe>.Instance);

            var first = await probe.ProbeAsync(TestContext.Current.CancellationToken);
            var second = await probe.ProbeAsync(TestContext.Current.CancellationToken);

            first.Readiness.Should().Be(DependencyReadiness.Ready);
            second.Readiness.Should().Be(DependencyReadiness.Ready);
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

    private sealed record TestPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }
}
