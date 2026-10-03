using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Dependencies;

using Neovolve.Logging.Xunit;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed class LocalApplicationLogReaderTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void GetAvailableLogs_returns_empty_when_the_log_directory_does_not_exist()
    {
        using var logger = output.BuildLoggerFor<LocalApplicationLogReader>();
        var reader = CreateReader(logger);

        reader.GetAvailableLogs().Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableLogs_returns_valid_daily_logs_newest_first()
    {
        var directory = CreateLogDirectory();
        File.WriteAllText(Path.Combine(directory, "kora-20261001.log"), "older");
        File.WriteAllText(Path.Combine(directory, "kora-20261003.log"), "newer");
        File.WriteAllText(Path.Combine(directory, "kora-20260230.log"), "invalid date");
        File.WriteAllText(Path.Combine(directory, "xora-20261003.log"), "wrong prefix");
        File.WriteAllText(Path.Combine(directory, "kora-20261003.txt"), "wrong suffix");
        File.WriteAllText(Path.Combine(directory, "kora-short.log"), "wrong length");
        using var logger = output.BuildLoggerFor<LocalApplicationLogReader>();
        var reader = CreateReader(logger);

        var logs = reader.GetAvailableLogs();

        logs.Select(log => log.FileName).Should().Equal(
            "kora-20261003.log",
            "kora-20261001.log");
        logs[0].Date.Should().Be(new DateOnly(2026, 10, 3));
        logs[0].Length.Should().Be(5);
        logs[0].LastWriteTime.Should().NotBe(default);
    }

    [Fact]
    public async Task ReadTailAsync_returns_the_entire_small_log()
    {
        var directory = CreateLogDirectory();
        File.WriteAllText(Path.Combine(directory, "kora-20261003.log"), "small log");
        using var logger = output.BuildLoggerFor<LocalApplicationLogReader>();
        var reader = CreateReader(logger);

        var content = await reader.ReadTailAsync(
            "kora-20261003.log",
            100,
            TestContext.Current.CancellationToken);

        content.Should().Be("small log");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    public async Task ReadTailAsync_returns_only_the_requested_tail(int contentLength)
    {
        var directory = CreateLogDirectory();
        File.WriteAllText(
            Path.Combine(directory, "kora-20261003.log"),
            new string('x', contentLength));
        using var logger = output.BuildLoggerFor<LocalApplicationLogReader>();
        var reader = CreateReader(logger);

        var content = await reader.ReadTailAsync(
            "kora-20261003.log",
            5,
            TestContext.Current.CancellationToken);

        content.Should().Be("xxxxx");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(LocalApplicationLogReader.MaximumReadCharacters + 1)]
    public async Task ReadTailAsync_rejects_out_of_range_character_limits(int maximumCharacters)
    {
        using var logger = output.BuildLoggerFor<LocalApplicationLogReader>();
        var reader = CreateReader(logger);

        var action = () => reader.ReadTailAsync(
            "kora-20261003.log",
            maximumCharacters,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-log.txt")]
    [InlineData("xora-20261003.log")]
    [InlineData("kora-20260230.log")]
    [InlineData("..\\kora-20261003.log")]
    public async Task ReadTailAsync_rejects_invalid_file_names(string fileName)
    {
        using var logger = output.BuildLoggerFor<LocalApplicationLogReader>();
        var reader = CreateReader(logger);

        var action = () => reader.ReadTailAsync(
            fileName,
            100,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReadTailAsync_reports_a_missing_daily_log()
    {
        using var logger = output.BuildLoggerFor<LocalApplicationLogReader>();
        var reader = CreateReader(logger);

        var action = () => reader.ReadTailAsync(
            "kora-20261003.log",
            100,
            TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<FileNotFoundException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string CreateLogDirectory()
    {
        var directory = Path.Combine(root, "Logs");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private LocalApplicationLogReader CreateReader(
        Microsoft.Extensions.Logging.ILogger<LocalApplicationLogReader> logger) =>
        new(new TestPaths(root, Path.Combine(root, "Roaming")), logger);

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}
