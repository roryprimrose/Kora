using System.Text;
using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class LocalFileTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSamePhysicalSourceRefreshChangesContentDigestAndExactCitationsOnlyAfterFreshConfirmation(bool folder)
    {
        var directory = Path.Combine(root, "guides");
        Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "a.md");
        var second = Path.Combine(directory, "b.txt");
        await File.WriteAllTextAsync(first, "old alpha", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "old beta", TestContext.Current.CancellationToken);
        using var host = Host();
        var audit = new FolderAudit();
        using var preview = new LocalFilePreview(Inspector(), audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        await SelectRefreshSource(preview, folder, directory, first);
        await ConfirmRefreshSource(preview, folder);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        byte[] changed = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("# Updated\r\nnew marker 🙂")];
        await File.WriteAllBytesAsync(folder ? second : first, changed, TestContext.Current.CancellationToken);
        if (folder)
        {
            File.Delete(first);
            await File.WriteAllTextAsync(Path.Combine(directory, "c.md"), "new marker gamma", TestContext.Current.CancellationToken);
        }
        var refresh = new LocalFileRefresh(preview);
        (await (folder ? refresh.ExecuteAsync(source!.Reference, () => true, TestContext.Current.CancellationToken)
            : refresh.ExecuteAsync(file!.Reference, () => true, TestContext.Current.CancellationToken))).Should().Be(LocalFileOutcome.Reviewed);
        preview.Current.Should().BeNull();
        preview.CurrentFolder.Should().BeNull();
        var selectedPath = folder ? second : first;
        var write = () => File.Open(selectedPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        write.Should().Throw<IOException>();
        if (folder)
        {
            preview.FolderReview!.Metadata.DirectoryIdentity.Should().Be(source!.Review.Metadata.DirectoryIdentity);
            preview.FolderReview.Metadata.Files.Select(item => item.CanonicalPath).Should().Equal(second, Path.Combine(directory, "c.md"));
        }
        else { preview.Review!.Metadata.FileIdentity.Should().Be(file!.Review.Metadata.FileIdentity); }
        await ConfirmRefreshSource(preview, folder);
        var current = folder ? preview.CurrentFolder!.Files[0] : preview.Current!;
        current.Text.Should().Be("# Updated\r\nnew marker 🙂");
        current.Digest.Should().Be(LocalFilePolicy.Digest(changed));
        current.Reference.SourceId.Should().Be(file?.Reference.SourceId ?? source!.Reference.SourceId);
        var search = new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, TimeProvider.System, NullLogger<LocalFileSearch>.Instance);
        var result = folder
            ? await search.ExecuteAsync(preview.CurrentFolder!.Reference, "new", () => true, TestContext.Current.CancellationToken)
            : await search.ExecuteAsync(current.Reference, "new", () => true, TestContext.Current.CancellationToken);
        result.Citations.Should().Contain(item => item.Source == current.Reference && item.Excerpt == current.Text);
        var stale = folder
            ? await search.ExecuteAsync(source!.Reference, "old", () => true, TestContext.Current.CancellationToken)
            : await search.ExecuteAsync(file!.Reference, "old", () => true, TestContext.Current.CancellationToken);
        stale.Outcome.Should().Be(LocalFileSearchOutcome.Stale);
        (await File.ReadAllBytesAsync(selectedPath, TestContext.Current.CancellationToken)).Should().Equal(changed);
    }

    [Theory]
    [InlineData(false, "replace")]
    [InlineData(true, "replace")]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "hard-link")]
    [InlineData(false, "hidden")]
    [InlineData(false, "oversize")]
    [InlineData(true, "subdirectory")]
    [InlineData(true, "unsupported")]
    [InlineData(true, "hard-link")]
    public async Task RealReopenPolicyNeverRebindsRootIdentityOrSilentlyExcludesUnsafeNewInventory(bool folder, string change)
    {
        var directory = Path.Combine(root, "guides");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source.md");
        await File.WriteAllTextAsync(path, "synthetic", TestContext.Current.CancellationToken);
        using var host = Host();
        using var preview = new LocalFilePreview(Inspector(), new FolderAudit(), TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        await SelectRefreshSource(preview, folder, directory, path);
        await ConfirmRefreshSource(preview, folder);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        switch (change)
        {
            case "replace":
                if (folder)
                {
                    Directory.Move(directory, directory + "-original");
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(path, "replacement", TestContext.Current.CancellationToken);
                }
                else
                {
                    File.Move(path, path + ".original");
                    await File.WriteAllTextAsync(path, "replacement", TestContext.Current.CancellationToken);
                }
                break;
            case "missing":
                if (folder) { Directory.Move(directory, directory + "-original"); }
                else { File.Move(path, path + ".original"); }
                break;
            case "hard-link": CreateHardLink(Path.Combine(root, "alias.md"), path, nint.Zero).Should().BeTrue(); break;
            case "hidden": File.SetAttributes(path, FileAttributes.Hidden); break;
            case "oversize": await File.WriteAllBytesAsync(path, new byte[LocalFilePolicy.MaximumBytes + 1], TestContext.Current.CancellationToken); break;
            case "subdirectory": Directory.CreateDirectory(Path.Combine(directory, "nested.md")); break;
            case "unsupported": await File.WriteAllBytesAsync(Path.Combine(directory, "binary.png"), [0, 1], TestContext.Current.CancellationToken); break;
        }
        var refresh = new LocalFileRefresh(preview);
        (await (folder ? refresh.ExecuteAsync(source!.Reference, () => true, TestContext.Current.CancellationToken)
            : refresh.ExecuteAsync(file!.Reference, () => true, TestContext.Current.CancellationToken))).Should().Be(LocalFileOutcome.Unavailable);
        preview.Current.Should().BeNull();
        preview.CurrentFolder.Should().BeNull();
        preview.Review.Should().BeNull();
        preview.FolderReview.Should().BeNull();
        await preview.WaitForQuiescenceAsync();
        if (change is "hidden") { File.SetAttributes(path, FileAttributes.Normal); }
    }

    private static Task<LocalFileOutcome> SelectRefreshSource(LocalFilePreview preview, bool folder, string directory, string file) => folder
        ? preview.SelectFolderAsync(new FolderPicker(directory), () => true, TestContext.Current.CancellationToken)
        : preview.SelectAsync(new RefreshPicker(file), () => true, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(false, "utf8")]
    [InlineData(true, "utf8")]
    [InlineData(false, "binary")]
    [InlineData(true, "binary")]
    [InlineData(true, "inventory")]
    public async Task FreshMetadataNeverGuessesEncodingAndCaptureRejectsEntireInvalidOrUnstableInventory(bool folder, string change)
    {
        var directory = Path.Combine(root, "guides");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "a.md");
        var unsafePath = folder ? Path.Combine(directory, "z.md") : path;
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        if (folder) { await File.WriteAllTextAsync(unsafePath, "old", TestContext.Current.CancellationToken); }
        using var host = Host();
        using var preview = new LocalFilePreview(Inspector(), new FolderAudit(), TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        await SelectRefreshSource(preview, folder, directory, path);
        await ConfirmRefreshSource(preview, folder);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        await File.WriteAllBytesAsync(unsafePath, change is "utf8" ? [0xc0, 0xaf] : change is "binary" ? [0] : [0x61],
            TestContext.Current.CancellationToken);
        var refresh = new LocalFileRefresh(preview);
        (await (folder ? refresh.ExecuteAsync(source!.Reference, () => true, TestContext.Current.CancellationToken)
            : refresh.ExecuteAsync(file!.Reference, () => true, TestContext.Current.CancellationToken))).Should().Be(LocalFileOutcome.Reviewed);
        if (change is "inventory")
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "new.md"), "added after review", TestContext.Current.CancellationToken);
        }
        (await (folder ? preview.ConfirmFolderAsync(preview.FolderReview!.ReviewId, () => true, TestContext.Current.CancellationToken)
            : preview.ConfirmAsync(preview.Review!.ReviewId, () => true, TestContext.Current.CancellationToken)))
            .Should().Be(change is "utf8" ? LocalFileOutcome.InvalidText : LocalFileOutcome.Unavailable);
        preview.Current.Should().BeNull();
        preview.CurrentFolder.Should().BeNull();
        await preview.WaitForQuiescenceAsync();
        Directory.Move(directory, directory + "-released");
    }

    private static async Task ConfirmRefreshSource(LocalFilePreview preview, bool folder)
    {
        (await (folder ? preview.ConfirmFolderAsync(preview.FolderReview!.ReviewId, () => true, TestContext.Current.CancellationToken)
            : preview.ConfirmAsync(preview.Review!.ReviewId, () => true, TestContext.Current.CancellationToken))).Should().Be(LocalFileOutcome.Admitted);
    }

    private sealed class RefreshPicker(string path) : IUserFilePicker
    {
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(path);
    }
}
