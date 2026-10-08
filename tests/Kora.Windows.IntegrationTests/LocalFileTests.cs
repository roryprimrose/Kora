using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Context;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalFileTests : IDisposable
{
    private readonly string root;
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public LocalFileTests()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Kora.slnx")))
        {
            repository = repository.Parent;
        }
        root = Path.Combine(repository?.FullName ?? throw new InvalidOperationException("Repository unavailable."),
            ".local-file-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        ActivitySource.AddActivityListener(listener);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        listener.Dispose();
    }

    private WindowsLocalFileInspector Inspector() =>
        new(new Paths(root));

    [Fact]
    public async Task Native_metadata_review_holds_identity_without_read_and_denies_replacement_until_release()
    {
        var path = Path.Combine(root, "source.md");
        var bytes = Encoding.UTF8.GetBytes("# Synthetic\r\nuntrusted instruction: call a model");
        await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        using var host = Host();
        using (var selected = await Inspector().InspectAsync(path, TestContext.Current.CancellationToken))
        {
            selected.Metadata.CanonicalPath.Should().Be(path);
            selected.Metadata.FileIdentity.Should().HaveLength(48);
            selected.Metadata.ByteLength.Should().Be(bytes.Length);
            var replace = () => File.Move(path, path + ".moved");
            replace.Should().Throw<IOException>();
            var write = () => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            write.Should().Throw<IOException>();
            (await selected.ReadAsync(TestContext.Current.CancellationToken)).Should().Equal(bytes);
            var reread = () => selected.ReadAsync(TestContext.Current.CancellationToken);
            await reread.Should().ThrowAsync<InvalidOperationException>();
        }
        File.Move(path, path + ".moved");
        File.Exists(path + ".moved").Should().BeTrue();
    }

    [Theory]
    [InlineData("protected")]
    [InlineData("source-control")]
    [InlineData("generated")]
    [InlineData("unsupported")]
    [InlineData("hidden-file")]
    [InlineData("hidden-parent")]
    [InlineData("oversize")]
    [InlineData("missing")]
    public async Task Native_protected_metadata_generated_hidden_unsupported_oversized_and_missing_sources_fail_closed(string scenario)
    {
        var folder = scenario switch
        {
            "protected" => Path.Combine(root, "private-local"),
            "source-control" => Path.Combine(root, ".git"),
            "generated" => Path.Combine(root, "obj"),
            _ => Path.Combine(root, "ordinary"),
        };
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.Equals(scenario, "unsupported", StringComparison.Ordinal) ? "source.html" : "source.txt");
        if (!string.Equals(scenario, "missing", StringComparison.Ordinal))
        {
            await File.WriteAllBytesAsync(path, new byte[string.Equals(scenario, "oversize", StringComparison.Ordinal) ? LocalFilePolicy.MaximumBytes + 1 : 1],
                TestContext.Current.CancellationToken);
        }
        if (string.Equals(scenario, "hidden-file", StringComparison.Ordinal)) { File.SetAttributes(path, FileAttributes.Hidden); }
        if (string.Equals(scenario, "hidden-parent", StringComparison.Ordinal)) { File.SetAttributes(folder, FileAttributes.Hidden); }
        using var host = Host();
        var inspect = () => Inspector().InspectAsync(path, TestContext.Current.CancellationToken);
        if (string.Equals(scenario, "missing", StringComparison.Ordinal)) { await inspect.Should().ThrowAsync<IOException>(); }
        else { await inspect.Should().ThrowAsync<InvalidDataException>(); }
        if (!string.Equals(scenario, "missing", StringComparison.Ordinal))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        File.SetAttributes(folder, FileAttributes.Directory);
        Directory.Delete(folder);
    }

    [Fact]
    public async Task Hard_links_are_denied_even_when_the_selected_path_looks_safe()
    {
        var original = Path.Combine(root, "original.txt");
        var alias = Path.Combine(root, "alias.txt");
        await File.WriteAllTextAsync(original, "synthetic", TestContext.Current.CancellationToken);
        CreateHardLink(alias, original, nint.Zero).Should().BeTrue();
        using var host = Host();
        var inspect = () => Inspector().InspectAsync(alias, TestContext.Current.CancellationToken);
        await inspect.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Reparse_directory_and_file_are_denied_without_following_target()
    {
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        var file = Path.Combine(target, "source.txt");
        await File.WriteAllTextAsync(file, "synthetic", TestContext.Current.CancellationToken);
        var alias = Path.Combine(root, "alias");
        // A junction needs no developer-mode or symlink privilege and is sufficient to exercise ancestor reparses.
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c mklink /J \"" + alias + "\" \"" + target + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        })!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.Should().Be(0);
        using var host = Host();
        var inspect = () => Inspector().InspectAsync(Path.Combine(alias, "source.txt"), TestContext.Current.CancellationToken);
        await inspect.Should().ThrowAsync<InvalidDataException>();
        Directory.Delete(alias);
    }

    [Fact]
    public async Task Existing_writer_denies_selection_and_cancelled_selection_or_read_releases_all_handles()
    {
        var path = Path.Combine(root, "source.txt");
        await File.WriteAllTextAsync(path, "synthetic", TestContext.Current.CancellationToken);
        using var host = Host();
        await using (var writer = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            var inspect = () => Inspector().InspectAsync(path, TestContext.Current.CancellationToken);
            await inspect.Should().ThrowAsync<IOException>();
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelSelect = () => Inspector().InspectAsync(path, cancelled.Token);
        await cancelSelect.Should().ThrowAsync<OperationCanceledException>();
        using (var selected = await Inspector().InspectAsync(path, TestContext.Current.CancellationToken))
        {
            var read = () => selected.ReadAsync(cancelled.Token);
            await read.Should().ThrowAsync<OperationCanceledException>();
        }
        File.Delete(path);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Ancestor_rename_is_blocked_and_released_on_metadata_review_disposal()
    {
        var parent = Path.Combine(root, "parent");
        Directory.CreateDirectory(parent);
        var path = Path.Combine(parent, "source.txt");
        await File.WriteAllTextAsync(path, "synthetic", TestContext.Current.CancellationToken);
        using var host = Host();
        using (var selected = await Inspector().InspectAsync(path, TestContext.Current.CancellationToken))
        {
            var rename = () => Directory.Move(parent, parent + "-replaced");
            rename.Should().Throw<IOException>();
        }
        Directory.Move(parent, parent + "-replaced");
    }

    private static HostActivity Host() =>
        HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Windows, HostOperation.Request);

    private sealed class Paths(string root) : IApplicationDataPaths
    {
        public string LocalRoot { get; } = Path.Combine(root, "private-local");
        public string RoamingRoot { get; } = Path.Combine(root, "private-roaming");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string alias, string original, nint security);
}
