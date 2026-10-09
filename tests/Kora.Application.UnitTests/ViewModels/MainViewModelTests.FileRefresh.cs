using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivatedRefreshRequestsOnlyNewNativeReviewAndVoiceWithdrawalInvalidatesItsControl(bool folder)
    {
        var fixture = new Fixture();
        var files = new FakeFileInspector();
        var folders = new FolderInspector();
        using var preview = new LocalFilePreview(folder ? folders : files, fixture.Audit, TimeProvider.System,
            NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker(), refresh: new(preview));
        if (folder) { await fixture.ViewModel.PreviewFolderAsync(); }
        else { await fixture.ViewModel.PreviewFileAsync(); }
        var original = fixture.ViewModel.FileReview?.Request ?? fixture.ViewModel.FolderReview!.Request;
        await fixture.ViewModel.ConfirmFilePreviewAsync(fixture.ViewModel.FileReview?.ReviewId ?? fixture.ViewModel.FolderReview!.ReviewId);
        await fixture.RaiseActivatedTranscriptAsync(folder ? "Kora, refresh folder" : "Kora, refresh file", 1);
        fixture.ViewModel.ResponseTitle.Should().Contain("Reviewed");
        (fixture.ViewModel.FileReview?.Request ?? fixture.ViewModel.FolderReview!.Request).Should().Be(original);
        var id = fixture.ViewModel.FileReview?.ReviewId ?? fixture.ViewModel.FolderReview!.ReviewId;
        files.Reads.Should().Be(folder ? 0 : 1);
        folders.Reads.Should().Be(folder ? 2 : 0);
        await fixture.ViewModel.SetVoiceConsentAsync(false);
        await fixture.ViewModel.ConfirmFilePreviewAsync(id);
        fixture.ViewModel.ResponseTitle.Should().Contain("Stale");
        fixture.ViewModel.FileRevision.Should().BeNull();
        fixture.ViewModel.FolderRevision.Should().BeNull();
        files.Reads.Should().Be(folder ? 0 : 1);
        folders.Reads.Should().Be(folder ? 2 : 0);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeAndExactCommandRefreshReviewAgainBeforeReadingAndNeverPersistOrSubmitContent(bool folder, bool native)
    {
        var fixture = new Fixture();
        var files = new FakeFileInspector();
        var folders = new FolderInspector();
        using var preview = new LocalFilePreview(folder ? folders : files, fixture.Audit, TimeProvider.System,
            NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker(), refresh: new(preview));
        if (folder) { await fixture.ViewModel.PreviewFolderAsync(); }
        else { await fixture.ViewModel.PreviewFileAsync(); }
        var previousReview = fixture.ViewModel.FileReview?.ReviewId ?? fixture.ViewModel.FolderReview!.ReviewId;
        await fixture.ViewModel.ConfirmFilePreviewAsync(previousReview);
        var file = fixture.ViewModel.FileRevision;
        var source = fixture.ViewModel.FolderRevision;
        using (var system = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            if (folder) { await fixture.ViewModel.RefreshFolderPreviewAsync(source!.Reference); }
            else { await fixture.ViewModel.RefreshFilePreviewAsync(file!.Reference); }
            fixture.ViewModel.ResponseTitle.Should().Contain("Stale");
        }
        if (native)
        {
            if (folder) { await fixture.ViewModel.RefreshFolderPreviewAsync(source!.Reference with { RevisionId = Guid.NewGuid() }); }
            else { await fixture.ViewModel.RefreshFilePreviewAsync(file!.Reference with { RevisionId = Guid.NewGuid() }); }
            fixture.ViewModel.ResponseTitle.Should().Contain("Stale");
            if (folder) { await fixture.ViewModel.RefreshFolderPreviewAsync(source!.Reference); }
            else { await fixture.ViewModel.RefreshFilePreviewAsync(file!.Reference); }
        }
        else { await fixture.RunAsync(folder ? "refresh folder" : "refresh file"); }
        fixture.ViewModel.ResponseTitle.Should().Contain("Reviewed");
        fixture.ViewModel.ResponseBody.Should().Contain("retires the old preview").And.Contain("confirm again");
        fixture.ViewModel.FileRevision.Should().BeNull();
        fixture.ViewModel.FolderRevision.Should().BeNull();
        files.Reads.Should().Be(folder ? 0 : 1);
        folders.Reads.Should().Be(folder ? 2 : 0);
        var fresh = fixture.ViewModel.FileReview?.ReviewId ?? fixture.ViewModel.FolderReview!.ReviewId;
        fresh.Should().NotBe(previousReview);
        await fixture.ViewModel.ConfirmFilePreviewAsync(previousReview);
        fixture.ViewModel.ResponseTitle.Should().Contain("Stale");
        await fixture.ViewModel.ConfirmFilePreviewAsync(fresh);
        fixture.ViewModel.ResponseTitle.Should().Contain("Admitted");
        if (folder)
        {
            fixture.ViewModel.FolderRevision!.Reference.SourceId.Should().Be(source!.Reference.SourceId);
            fixture.ViewModel.FolderRevision.Reference.RevisionId.Should().NotBe(source.Reference.RevisionId);
        }
        else
        {
            fixture.ViewModel.FileRevision!.Reference.SourceId.Should().Be(file!.Reference.SourceId);
            fixture.ViewModel.FileRevision.Reference.RevisionId.Should().NotBe(file.Reference.RevisionId);
        }
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ClipboardReader.Calls.Should().Be(0);
        fixture.ViewModel.Transcript.Should().NotContain(FakeFileInspector.Text).And.NotContain(FolderInspector.Text);
        fixture.ViewModel.ResponseBody.Should().NotContain(FakeFileInspector.Text).And.NotContain(FolderInspector.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingUnboundStaleAndSystemRefreshNeverReopenOrChooseAnotherSource(bool folder)
    {
        var fixture = new Fixture();
        var unknownFile = new LocalFileReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "unknown");
        var unknownFolder = new LocalFolderReference(Guid.NewGuid(), Guid.NewGuid());
        await fixture.ViewModel.RefreshFilePreviewAsync(unknownFile);
        await fixture.ViewModel.RefreshFolderPreviewAsync(unknownFolder);
        fixture.ViewModel.ResponseTitle.Should().Contain("Stale");
        await fixture.RunAsync(folder ? "refresh folder" : "refresh file");
        fixture.ViewModel.ResponseTitle.Should().Contain("Denied");
        var files = new FakeFileInspector();
        var folders = new FolderInspector();
        using var preview = new LocalFilePreview(folder ? folders : files, fixture.Audit, TimeProvider.System,
            NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        if (folder) { await fixture.ViewModel.PreviewFolderAsync(); }
        else { await fixture.ViewModel.PreviewFileAsync(); }
        await fixture.ViewModel.ConfirmFilePreviewAsync(fixture.ViewModel.FileReview?.ReviewId ?? fixture.ViewModel.FolderReview!.ReviewId);
        var file = fixture.ViewModel.FileRevision;
        var source = fixture.ViewModel.FolderRevision;
        await fixture.RunAsync(folder ? "refresh folder" : "refresh file");
        fixture.ViewModel.ResponseTitle.Should().Contain("Stale");
        using (var system = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request))
        {
            if (folder) { await fixture.ViewModel.RefreshFolderPreviewAsync(source!.Reference); }
            else { await fixture.ViewModel.RefreshFilePreviewAsync(file!.Reference); }
            fixture.ViewModel.ResponseTitle.Should().Contain("Stale");
        }
        await fixture.ViewModel.RefreshFilePreviewAsync(unknownFile);
        await fixture.ViewModel.RefreshFolderPreviewAsync(unknownFolder);
        await fixture.RunAsync(folder ? "refresh file" : "refresh folder");
        fixture.ViewModel.ResponseTitle.Should().Contain("Stale");
        foreach (var command in new[] { @"refresh file C:\private.txt", @"refresh folder C:\private", "refresh file now", "refresh folders" })
        {
            await fixture.RunAsync(command);
            fixture.ViewModel.ResponseTitle.Should().Contain("Denied");
        }
        files.Reads.Should().Be(folder ? 0 : 1);
        folders.Reads.Should().Be(folder ? 2 : 0);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRefreshRefusalIsExplicitRetiresOldPreviewAndNeverClaimsSuccess(bool folder)
    {
        var fixture = new Fixture();
        using var preview = new LocalFilePreview(new RefusingRefreshInspector(folder ? new FolderInspector() : new FakeFileInspector()),
            fixture.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker(), refresh: new(preview));
        if (folder) { await fixture.ViewModel.PreviewFolderAsync(); }
        else { await fixture.ViewModel.PreviewFileAsync(); }
        await fixture.ViewModel.ConfirmFilePreviewAsync(fixture.ViewModel.FileReview?.ReviewId ?? fixture.ViewModel.FolderReview!.ReviewId);
        if (folder) { await fixture.ViewModel.RefreshFolderPreviewAsync(fixture.ViewModel.FolderRevision!.Reference); }
        else { await fixture.ViewModel.RefreshFilePreviewAsync(fixture.ViewModel.FileRevision!.Reference); }
        fixture.ViewModel.ResponseTitle.Should().Contain("Unavailable");
        fixture.ViewModel.ResponseBody.Should().Contain("Failure/cancel leaves no preview").And.Contain("native picker");
        fixture.ViewModel.FileRevision.Should().BeNull();
        fixture.ViewModel.FolderRevision.Should().BeNull();
        fixture.ViewModel.FileReview.Should().BeNull();
        fixture.ViewModel.FolderReview.Should().BeNull();
        await preview.WaitForQuiescenceAsync();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    private sealed class RefusingRefreshInspector(ILocalFileInspector original) : ILocalFileInspector
    {
        private int inspections;
        public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken) =>
            ++inspections == 1 ? original.InspectAsync(selectedPath, cancellationToken)
                : Task.FromException<ILocalFileSelection>(new IOException("Synthetic source missing."));
        public Task<ILocalFolderSelection> InspectFolderAsync(string selectedPath, CancellationToken cancellationToken) =>
            ++inspections == 1 ? original.InspectFolderAsync(selectedPath, cancellationToken)
                : Task.FromException<ILocalFolderSelection>(new IOException("Synthetic source missing."));
    }
}
