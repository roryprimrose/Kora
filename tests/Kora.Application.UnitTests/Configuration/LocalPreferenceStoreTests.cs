using System.Globalization;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class LocalPreferenceStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"Kora.PreferenceStoreTests.{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_returns_null_when_the_preference_does_not_exist(bool directoryExists)
    {
        var store = CreateStore();
        if (directoryExists) { Directory.CreateDirectory(Path.Combine(root, "Preferences")); }

        store.ReadText("missing.txt").Should().BeNull();
        store.ReadText("missing.txt", 32).Should().BeNull();
        store.ReadLines("missing.txt").Should().BeNull();
    }

    [Fact]
    public void A_directory_at_the_preference_path_is_unavailable_not_missing()
    {
        var store = CreateStore();
        var path = Path.Combine(root, "Preferences", "value.txt");
        Directory.CreateDirectory(path);

        var readText = () => store.ReadText("value.txt");
        var readBoundedText = () => store.ReadText("value.txt", 32);
        var readLines = () => store.ReadLines("value.txt");

        readText.Should().Throw<UnauthorizedAccessException>();
        readBoundedText.Should().Throw<UnauthorizedAccessException>();
        readLines.Should().Throw<UnauthorizedAccessException>();
        Directory.Exists(path).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delete_is_idempotent_when_the_preference_does_not_exist(bool directoryExists)
    {
        var store = CreateStore();
        if (directoryExists) { Directory.CreateDirectory(Path.Combine(root, "Preferences")); }

        store.Delete("missing.txt");
        store.Delete("missing.txt");

        store.ReadText("missing.txt").Should().BeNull();
        Directory.Exists(Path.Combine(root, "Preferences")).Should().Be(directoryExists);
    }

    [Theory]
    [InlineData("session-retention.txt")]
    [InlineData("session-retention-unconfirmed.txt")]
    public void Obstructed_saved_preference_or_marker_requires_explicit_repair_before_fresh_confirmation(string fileName)
    {
        var paths = new TestPaths(root);
        var preferences = new LocalSessionRetentionPreferences(paths);
        var settings = new SessionRetentionSettings(2, 40);
        preferences.BeginWrite();
        preferences.Save(settings);
        preferences.ConfirmWrite();
        var path = Path.Combine(root, "Preferences", fileName);
        File.Delete(path);
        Directory.CreateDirectory(path);

        var load = () => new LocalSessionRetentionPreferences(paths).Load();
        Action clear = string.Equals(fileName, "session-retention.txt", StringComparison.Ordinal)
            ? preferences.Reset : preferences.ConfirmWrite;

        load.Should().Throw<UnauthorizedAccessException>();
        clear.Should().Throw<UnauthorizedAccessException>();
        Directory.Exists(path).Should().BeTrue();

        Directory.Delete(path);
        preferences.BeginWrite();
        preferences.Save(settings);
        preferences.ReadBack().Should().Be(settings);
        load.Should().Throw<InvalidDataException>();
        preferences.ConfirmWrite();

        new LocalSessionRetentionPreferences(paths).Load().Should().Be(settings);
        Directory.GetFiles(Path.Combine(root, "Preferences"), "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Obstructed_deletion_reports_failed_audit_without_claiming_success()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var store = CreateStore();
        var path = Path.Combine(root, "Preferences", "value.txt");
        Directory.CreateDirectory(path);
        var audit = new RecordingAudit();
        var writer = new AuditedPreferenceWriter(audit);

        var delete = () => writer.WriteAsync(() => store.Delete("value.txt"),
            "fixture.preference-reset", SecurityAuditInitiator.LocalUser, RequestOrigin.LocalUi,
            () => true);

        await delete.Should().ThrowAsync<UnauthorizedAccessException>();
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        audit.Events.Select(item => item.CorrelationId).Distinct().Should().ContainSingle();
        Directory.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Write_replaces_existing_content_without_leaving_temporary_files()
    {
        var store = CreateStore();

        store.WriteText("value.txt", "first");
        store.WriteText("value.txt", "second");

        store.ReadText("value.txt").Should().Be("second");
        Directory.GetFiles(
            Path.Combine(root, "Preferences"),
            "*.tmp",
            SearchOption.TopDirectoryOnly).Should().BeEmpty();
    }

    [Fact]
    public void Failed_write_removes_the_temporary_file()
    {
        var store = CreateStore();
        var preferencesDirectory = Path.Combine(root, "Preferences");
        Directory.CreateDirectory(Path.Combine(preferencesDirectory, "value.txt"));

        var action = () => store.WriteText("value.txt", "value");

        var exception = action.Should().Throw<Exception>().Which;
        (exception is IOException or UnauthorizedAccessException).Should().BeTrue();
        Directory.GetFiles(
            preferencesDirectory,
            "*.tmp",
            SearchOption.TopDirectoryOnly).Should().BeEmpty();
    }

    [Fact]
    public void WriteLines_and_delete_use_the_same_preference_boundary()
    {
        var store = CreateStore();

        store.WriteLines("values.txt", ["first", "second"]);

        store.ReadLines("values.txt").Should().Equal("first", "second");
        store.Delete("values.txt");
        store.ReadText("values.txt").Should().BeNull();
    }

    [Fact]
    public void Concurrent_writes_do_not_collide_or_leave_partial_files()
    {
        var store = CreateStore();

        Parallel.For(
            0,
            20,
            value => store.WriteText(
                "value.txt",
                value.ToString(CultureInfo.InvariantCulture)));

        int.Parse(
            store.ReadText("value.txt")!,
            CultureInfo.InvariantCulture).Should().BeInRange(0, 19);
        Directory.GetFiles(
            Path.Combine(root, "Preferences"),
            "*.tmp",
            SearchOption.TopDirectoryOnly).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("nested/value.txt")]
    [InlineData(@"nested\value.txt")]
    [InlineData(@"C:value.txt")]
    [InlineData(".")]
    [InlineData("..")]
    public void Operations_reject_paths_outside_the_preference_directory(string fileName)
    {
        var action = () => CreateStore().WriteText(fileName, "value");

        action.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private LocalPreferenceStore CreateStore() =>
        new(new TestPaths(root));

    private sealed record TestPaths(string LocalRoot) : IApplicationDataPaths
    {
        public string RoamingRoot => LocalRoot;
    }

    private sealed class RecordingAudit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}