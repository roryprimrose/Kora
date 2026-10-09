using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalFileTests
{
    [Fact]
    public async Task NativeFolderPinsEveryImmediateFileAndAdmitsImmutableCrossFileCitations()
    {
        var folder = Path.Combine(root, "guides");
        Directory.CreateDirectory(folder);
        var paths = new[] { Path.Combine(folder, "a.md"), Path.Combine(folder, "b.txt") };
        await File.WriteAllTextAsync(paths[0], "# First\r\nalpha\r\n", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(paths[1], [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("# Second\nalpha beta\n")],
            TestContext.Current.CancellationToken);
        using var host = Host();
        var audit = new FolderAudit();
        using var preview = new LocalFilePreview(Inspector(), audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        (await preview.SelectFolderAsync(new FolderPicker(folder), () => true, TestContext.Current.CancellationToken))
            .Should().Be(LocalFileOutcome.Reviewed);
        var review = preview.FolderReview!;
        review.Metadata.Files.Select(file => file.CanonicalPath).Should().Equal(paths);
        foreach (var path in paths)
        {
            var write = () => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            write.Should().Throw<IOException>();
            var rename = () => File.Move(path, path + ".moved");
            rename.Should().Throw<IOException>();
        }
        var moveFolder = () => Directory.Move(folder, folder + "-moved");
        moveFolder.Should().Throw<IOException>();
        (await preview.ConfirmFolderAsync(review.ReviewId, () => true, TestContext.Current.CancellationToken))
            .Should().Be(LocalFileOutcome.Admitted);
        var revision = preview.CurrentFolder!;
        var action = new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, TimeProvider.System,
            NullLogger<LocalFileSearch>.Instance);
        var result = await action.ExecuteAsync(revision.Reference, "alpha beta", () => true, TestContext.Current.CancellationToken);
        result.Citations.Select(citation => citation.DisplayIdentity).Should().Equal("b.txt", "a.md");
        result.Citations[0].Source.Should().Be(revision.Files[1].Reference);
        result.Citations[1].Source.Should().Be(revision.Files[0].Reference);
        await File.WriteAllTextAsync(paths[0], "replacement", TestContext.Current.CancellationToken);
        (await action.ExecuteAsync(revision.Reference, "alpha", () => true, TestContext.Current.CancellationToken))
            .Citations.Single(citation => citation.DisplayIdentity is "a.md").Excerpt.Should().Be("# First\r\nalpha\r\n");
        preview.Clear();
        await preview.WaitForQuiescenceAsync();
        File.Move(paths[1], paths[1] + ".moved");
    }

    [Theory]
    [InlineData("subdirectory")]
    [InlineData("text-named-directory")]
    [InlineData("source-control")]
    [InlineData("generated")]
    [InlineData("archive")]
    [InlineData("binary-extension")]
    [InlineData("hidden")]
    [InlineData("protected")]
    [InlineData("count")]
    [InlineData("combined")]
    [InlineData("per-file")]
    [InlineData("empty")]
    public async Task NativeFolderRejectsWholeSelectionForAnyInadmissibleItemOrBound(string scenario)
    {
        var folder = Path.Combine(root, scenario is "protected" ? "private-local" : "guides");
        Directory.CreateDirectory(folder);
        var valid = Path.Combine(folder, "valid.md");
        if (scenario is not "empty")
        {
            await File.WriteAllTextAsync(valid, "synthetic", TestContext.Current.CancellationToken);
        }
        var hostile = Path.Combine(folder, "hostile.txt");
        switch (scenario)
        {
            case "subdirectory": Directory.CreateDirectory(Path.Combine(folder, "nested")); break;
            case "text-named-directory": Directory.CreateDirectory(Path.Combine(folder, "nested.md")); break;
            case "source-control": Directory.CreateDirectory(Path.Combine(folder, ".git")); break;
            case "generated": Directory.CreateDirectory(Path.Combine(folder, "obj")); break;
            case "archive": await File.WriteAllTextAsync(Path.Combine(folder, "nested.zip"), "PK", TestContext.Current.CancellationToken); break;
            case "binary-extension": await File.WriteAllBytesAsync(Path.Combine(folder, "image.png"), [0, 1], TestContext.Current.CancellationToken); break;
            case "hidden": await File.WriteAllTextAsync(hostile, "synthetic", TestContext.Current.CancellationToken);
                File.SetAttributes(hostile, FileAttributes.Hidden); break;
            case "count":
                for (var index = 0; index < LocalFolderPolicy.MaximumFiles; index++)
                {
                    await File.WriteAllTextAsync(Path.Combine(folder, $"{index}.txt"), "", TestContext.Current.CancellationToken);
                }
                break;
            case "combined":
                for (var index = 0; index < 4; index++)
                {
                    await File.WriteAllBytesAsync(Path.Combine(folder, $"{index}.txt"), new byte[LocalFilePolicy.MaximumBytes],
                        TestContext.Current.CancellationToken);
                }
                break;
            case "per-file":
                await File.WriteAllBytesAsync(hostile, new byte[LocalFilePolicy.MaximumBytes + 1], TestContext.Current.CancellationToken); break;
        }
        using var host = Host();
        var inspect = () => Inspector().InspectFolderAsync(folder, TestContext.Current.CancellationToken);
        await inspect.Should().ThrowAsync<InvalidDataException>();
        if (scenario is "hidden") { File.SetAttributes(hostile, FileAttributes.Normal); }
        // Failed inspection must not leave earlier valid-file or ancestor handles pinned.
        if (File.Exists(valid)) { File.Move(valid, valid + ".moved"); }
        Directory.Move(folder, folder + "-released");
    }

    [Fact]
    public async Task NativeFolderRejectsHardLinkedItemWithoutLeakingEarlierHandles()
    {
        var original = Path.Combine(root, "original.txt");
        await File.WriteAllTextAsync(original, "synthetic", TestContext.Current.CancellationToken);
        var folder = Path.Combine(root, "guides");
        Directory.CreateDirectory(folder);
        var safe = Path.Combine(folder, "a.md");
        await File.WriteAllTextAsync(safe, "safe", TestContext.Current.CancellationToken);
        CreateHardLink(Path.Combine(folder, "z.txt"), original, nint.Zero).Should().BeTrue();
        using var host = Host();
        var inspect = () => Inspector().InspectFolderAsync(folder, TestContext.Current.CancellationToken);
        await inspect.Should().ThrowAsync<InvalidDataException>();
        File.Move(safe, safe + ".moved");
        Directory.Move(folder, folder + "-released");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFolderRejectsRootAndImmediateReparseDirectories(bool child)
    {
        var target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "source.txt"), "synthetic", TestContext.Current.CancellationToken);
        var folder = Path.Combine(root, "guides");
        Directory.CreateDirectory(folder);
        var alias = child ? Path.Combine(folder, "nested") : Path.Combine(root, "alias");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = "/c mklink /J \"" + alias + "\" \"" + target + "\"",
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        })!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.Should().Be(0);
        using var host = Host();
        var inspect = () => Inspector().InspectFolderAsync(child ? folder : alias, TestContext.Current.CancellationToken);
        await inspect.Should().ThrowAsync<InvalidDataException>();
        Directory.Delete(alias);
    }

    [Theory]
    [InlineData("added-file")]
    [InlineData("added-directory")]
    [InlineData("new-hard-link")]
    [InlineData("hidden-file")]
    [InlineData("hidden-root")]
    [InlineData("hidden-ancestor")]
    public async Task NativeFolderRevalidatesExactMembershipAndItemIdentityAtConfirmation(string change)
    {
        var folder = Path.Combine(root, "guides");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "source.txt");
        await File.WriteAllTextAsync(path, "synthetic", TestContext.Current.CancellationToken);
        using var host = Host();
        using (var selected = await Inspector().InspectFolderAsync(folder, TestContext.Current.CancellationToken))
        {
            switch (change)
            {
                case "added-file": await File.WriteAllTextAsync(Path.Combine(folder, "new.md"), "new", TestContext.Current.CancellationToken); break;
                case "added-directory": Directory.CreateDirectory(Path.Combine(folder, "nested")); break;
                case "hidden-file": File.SetAttributes(path, FileAttributes.Hidden); break;
                case "hidden-root": File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.Hidden); break;
                case "hidden-ancestor": File.SetAttributes(root, FileAttributes.Directory | FileAttributes.Hidden); break;
                default: CreateHardLink(Path.Combine(root, "alias.txt"), path, nint.Zero).Should().BeTrue(); break;
            }
            var validate = () => selected.ValidateAsync(TestContext.Current.CancellationToken);
            await validate.Should().ThrowAsync<InvalidDataException>();
        }
        File.SetAttributes(path, FileAttributes.Normal);
        File.SetAttributes(folder, FileAttributes.Directory);
        File.SetAttributes(root, FileAttributes.Directory);
        File.Move(path, path + ".released");
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("invalid-utf8")]
    [InlineData("cancel")]
    public async Task NativeFolderCaptureRejectsRenamedBinaryInvalidEncodingAndCancellationAtomically(string scenario)
    {
        var folder = Path.Combine(root, "guides");
        Directory.CreateDirectory(folder);
        var safe = Path.Combine(folder, "a.md");
        var bad = Path.Combine(folder, "b.txt");
        await File.WriteAllTextAsync(safe, "synthetic", TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(bad, scenario is "invalid-utf8" ? [0xc0, 0xaf] : [0x50, 0x4b, 0, 4],
            TestContext.Current.CancellationToken);
        using var host = Host();
        using var preview = new LocalFilePreview(Inspector(), new FolderAudit(), TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        await preview.SelectFolderAsync(new FolderPicker(folder), () => true, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        if (scenario is "cancel") { cancellation.Cancel(); }
        var outcome = await preview.ConfirmFolderAsync(preview.FolderReview!.ReviewId, () => true, cancellation.Token);
        outcome.Should().Be(scenario is "cancel" ? LocalFileOutcome.Cancelled
            : scenario is "invalid-utf8" ? LocalFileOutcome.InvalidText : LocalFileOutcome.Unavailable);
        preview.CurrentFolder.Should().BeNull();
        preview.IsQuiescent.Should().BeTrue();
        await preview.WaitForQuiescenceAsync();
        File.Move(safe, safe + ".released");
        File.Move(bad, bad + ".released");
    }

    [Fact]
    public async Task NativeFolderAdmitsExactFileAndCombinedByteMaximaAndMetadataOnlyEmptyFiles()
    {
        var folder = Path.Combine(root, "guides");
        Directory.CreateDirectory(folder);
        for (var index = 0; index < LocalFolderPolicy.MaximumFiles; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, $"{index:D2}.txt"),
                index < 4 ? new string('a', LocalFilePolicy.MaximumBytes) : "", TestContext.Current.CancellationToken);
        }
        using var host = Host();
        using var preview = new LocalFilePreview(Inspector(), new FolderAudit(), TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        await preview.SelectFolderAsync(new FolderPicker(folder), () => true, TestContext.Current.CancellationToken);
        preview.FolderReview!.Metadata.CombinedBytes.Should().Be(LocalFolderPolicy.MaximumCombinedBytes);
        (await preview.ConfirmFolderAsync(preview.FolderReview.ReviewId, () => true, TestContext.Current.CancellationToken))
            .Should().Be(LocalFileOutcome.Admitted);
        preview.CurrentFolder!.Files.Should().HaveCount(LocalFolderPolicy.MaximumFiles);
        preview.CurrentFolder.Files.Count(file => file.Text.Length == 0).Should().Be(28);
    }

    private sealed class FolderPicker(string path) : IUserFolderPicker
    {
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(path);
    }
    private sealed class FolderAudit : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) { }
    }
}
