using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Tools.UnitTests.Files;

[Collection("Host tracing")]
public sealed class LocalFileRefreshTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public LocalFileRefreshTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshCompleteMetadataReviewAndSeparateConfirmationPreserveSourceButReplaceAllExactReferences(bool folder)
    {
        using var host = Host();
        var inspector = new Inspector();
        var audit = new Audit();
        using var preview = Preview(inspector, audit);
        var refresh = new LocalFileRefresh(preview);
        await Admit(preview, folder);
        var previousFile = preview.Current;
        var previousFolder = preview.CurrentFolder;
        var previous = previousFile?.Review ?? previousFolder!.Files[0].Review;
        inspector.Text = "# Refreshed\nnew private marker";
        inspector.Names = ["b.md", "c.md"];
        (await Refresh(refresh, previousFile, previousFolder, token: TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Reviewed);
        inspector.Reads.Should().Be(folder ? 2 : 1);
        inspector.Inspections.Should().Be(2);
        preview.Current.Should().BeNull();
        preview.CurrentFolder.Should().BeNull();
        preview.IsQuiescent.Should().BeFalse();
        var fileReview = preview.Review;
        var folderReview = preview.FolderReview;
        var id = fileReview?.ReviewId ?? folderReview!.ReviewId;
        id.Should().NotBe(previous.ReviewId);
        (fileReview?.SourceId ?? folderReview!.SourceId).Should().Be(previous.SourceId);
        (fileReview?.Request ?? folderReview!.Request).Should().Be(previous.Request);
        if (folder)
        {
            folderReview!.PreviousMetadata.Should().BeSameAs(previousFolder!.Review.Metadata);
            folderReview.Metadata.Files.Select(file => file.CanonicalPath).Should().Equal(@"C:\Team\Guides\b.md", @"C:\Team\Guides\c.md");
            folderReview.Metadata.Files[0].FileIdentity.Should().Be("b.md");
            (await preview.ConfirmFolderAsync(previous.ReviewId, () => true, TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Stale);
        }
        else
        {
            fileReview!.PreviousMetadata.Should().BeSameAs(previous.Metadata);
            fileReview.Metadata.ByteLength.Should().Be(Encoding.UTF8.GetByteCount(inspector.Text));
            (await preview.ConfirmAsync(previous.ReviewId, () => true, TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Stale);
        }
        await Confirm(preview, folder, id);
        var files = folder ? preview.CurrentFolder!.Files : [preview.Current!];
        files.Should().OnlyContain(file => file.Text == inspector.Text && file.Review.SourceId == previous.SourceId
            && file.Review.Request == previous.Request && file.RevisionId != (previousFile != null ? previousFile.RevisionId : previousFolder!.Files[0].RevisionId)
            && file.ItemId != (previousFile != null ? previousFile.ItemId : previousFolder!.Files[0].ItemId));
        files.Select(file => file.Digest).Should().OnlyContain(digest => digest == LocalFilePolicy.Digest(Encoding.UTF8.GetBytes(inspector.Text)));
        files[0].Digest.Should().NotBe(previousFile?.Digest ?? previousFolder!.Files[0].Digest);
        var search = new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), audit, TimeProvider.System, NullLogger<LocalFileSearch>.Instance);
        var oldResult = folder
            ? await search.ExecuteAsync(previousFolder!.Reference, "private", () => true, TestContext.Current.CancellationToken)
            : await search.ExecuteAsync(previousFile!.Reference, "private", () => true, TestContext.Current.CancellationToken);
        oldResult.Outcome.Should().Be(LocalFileSearchOutcome.Stale);
        oldResult.Citations.Should().BeEmpty();
        var result = folder
            ? await search.ExecuteAsync(preview.CurrentFolder!.Reference, "new", () => true, TestContext.Current.CancellationToken)
            : await search.ExecuteAsync(preview.Current!.Reference, "new", () => true, TestContext.Current.CancellationToken);
        result.Citations.Select(citation => citation.Source).Should().Equal(files.Select(file => file.Reference));
        result.Citations.Should().OnlyContain(citation => citation.Excerpt == inspector.Text);
        inspector.Paths.Should().OnlyContain(path => path == (folder ? @"C:\Team\Guides" : @"C:\Team\guide.md"));
        inspector.Buffers.SelectMany(bytes => bytes).Should().OnlyContain(value => value == 0);
        audit.Events.Should().Contain(item => item.ActionId == (folder ? "folder.preview.refresh" : "file.preview.refresh")
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        string.Join(" ", audit.Events.Select(item => item.ActionId + item.TargetId + item.ReasonCode)).Should().NotContain("private").And.NotContain("C:\\");
        await preview.WaitForQuiescenceAsync();
    }

    [Theory]
    [InlineData(false, "identity")]
    [InlineData(true, "identity")]
    [InlineData(false, "path")]
    [InlineData(true, "path")]
    [InlineData(false, "missing")]
    [InlineData(true, "missing")]
    [InlineData(false, "oversize")]
    [InlineData(true, "bounds")]
    [InlineData(true, "subdirectory")]
    public async Task ChangedPhysicalRootPathMissingAndNewPolicyFailuresNeverReadOrRebind(bool folder, string refusal)
    {
        using var host = Host();
        var inspector = new Inspector();
        using var preview = Preview(inspector, new Audit());
        await Admit(preview, folder);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        inspector.Refusal = refusal;
        var outcome = await Refresh(new(preview), file, source, token: TestContext.Current.CancellationToken);
        outcome.Should().Be(refusal is "oversize" ? LocalFileOutcome.Oversize : LocalFileOutcome.Unavailable);
        inspector.Reads.Should().Be(folder ? 2 : 1);
        preview.Review.Should().BeNull();
        preview.FolderReview.Should().BeNull();
        preview.Current.Should().BeNull();
        preview.CurrentFolder.Should().BeNull();
        await preview.WaitForQuiescenceAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactReferenceOriginalSessionTaskOriginAndOriginalControlAreRequiredBeforeReopen(bool folder)
    {
        using var host = Host();
        var inspector = new Inspector();
        using var preview = Preview(inspector, new Audit());
        var refresh = new LocalFileRefresh(preview);
        (await refresh.ExecuteAsync(new LocalFileReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "unknown"),
            () => true, TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Stale);
        (await refresh.ExecuteAsync(new LocalFolderReference(Guid.NewGuid(), Guid.NewGuid()),
            () => true, TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Stale);
        var control = true;
        await Select(preview, folder, () => control);
        await Confirm(preview, folder, preview.Review?.ReviewId ?? preview.FolderReview!.ReviewId);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        foreach (var request in new[]
        {
            HostRequest.Create(RequestOrigin.LocalUi),
            new(new(Guid.NewGuid()), host.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            new(new(Guid.NewGuid()), host.Request.SessionId, host.Request.TaskId, RequestOrigin.HostSystem),
        })
        {
            using var other = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            (await Refresh(refresh, file, source, token: TestContext.Current.CancellationToken)).Should().Be(request.Origin is RequestOrigin.HostSystem
                ? LocalFileOutcome.Denied : LocalFileOutcome.Stale);
        }
        (await Refresh(refresh, file, source, () => false, TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Denied);
        var stale = folder
            ? await refresh.ExecuteAsync(source!.Reference with { RevisionId = Guid.NewGuid() }, () => true, TestContext.Current.CancellationToken)
            : await refresh.ExecuteAsync(file!.Reference with { Digest = "wrong" }, () => true, TestContext.Current.CancellationToken);
        stale.Should().Be(LocalFileOutcome.Stale);
        inspector.Inspections.Should().Be(1);
        control = false;
        (await Refresh(refresh, file, source, token: TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Stale);
        inspector.Inspections.Should().Be(1);
        preview.Current.Should().BeNull();
        preview.CurrentFolder.Should().BeNull();
    }

    [Theory]
    [InlineData(false, "clear")]
    [InlineData(true, "clear")]
    [InlineData(false, "original-control")]
    [InlineData(true, "original-control")]
    [InlineData(false, "current-control")]
    [InlineData(true, "current-control")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "dispose")]
    [InlineData(true, "dispose")]
    public async Task LateMetadataCannotSurviveRevocationAndOutstandingWorkNeverClaimsQuiescence(bool folder, string change)
    {
        using var host = Host();
        var inspector = new Inspector();
        using var preview = Preview(inspector, new Audit());
        var originalControl = true;
        var currentControl = true;
        await Select(preview, folder, () => originalControl);
        await Confirm(preview, folder, preview.Review?.ReviewId ?? preview.FolderReview!.ReviewId);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        inspector.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var pending = Refresh(new(preview), file, source, () => currentControl, cancellation.Token);
        await inspector.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        (await Refresh(new(preview), file, source, token: TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Busy);
        (await Select(preview, folder, () => true)).Should().Be(LocalFileOutcome.Busy);
        var search = new LocalFileSearch(preview, new LocalFileLexicalRetrieval(), new Audit(), TimeProvider.System, NullLogger<LocalFileSearch>.Instance);
        var result = folder
            ? await search.ExecuteAsync(source!.Reference, "private", () => true, TestContext.Current.CancellationToken)
            : await search.ExecuteAsync(file!.Reference, "private", () => true, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(LocalFileSearchOutcome.Busy);
        var wait = preview.WaitForQuiescenceAsync();
        wait.IsCompleted.Should().BeFalse();
        switch (change)
        {
            case "clear": preview.Clear(); break;
            case "dispose": preview.Dispose(); break;
            case "original-control": originalControl = false; break;
            case "current-control": currentControl = false; break;
            default: cancellation.Cancel(); break;
        }
        preview.IsQuiescent.Should().BeFalse();
        inspector.Gate.SetResult();
        (await pending).Should().Be(LocalFileOutcome.Cancelled);
        await wait;
        preview.Review.Should().BeNull();
        preview.FolderReview.Should().BeNull();
        inspector.Reads.Should().Be(folder ? 2 : 1);
        inspector.Disposals.Should().Be(2);
    }

    [Theory]
    [InlineData(false, "requested")]
    [InlineData(true, "requested")]
    [InlineData(false, "terminal")]
    [InlineData(true, "terminal")]
    [InlineData(false, "terminal-clear")]
    [InlineData(true, "terminal-clear")]
    [InlineData(false, "terminal-control")]
    [InlineData(true, "terminal-control")]
    [InlineData(false, "release")]
    [InlineData(true, "release")]
    public async Task RefreshAuditAndNativeReleaseFailuresSuppressReviewAndBlockUnverifiedHandoff(bool folder, string change)
    {
        using var host = Host();
        var inspector = new Inspector();
        var audit = new Audit();
        using var preview = Preview(inspector, audit);
        var control = true;
        await Admit(preview, folder);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        audit.Reject = change switch { "requested" => SecurityAuditOutcome.Requested, "terminal" => SecurityAuditOutcome.Succeeded, _ => null };
        audit.Callback = item =>
        {
            if (item.Outcome != SecurityAuditOutcome.Succeeded) { return; }
            preview.Review.Should().BeNull();
            preview.FolderReview.Should().BeNull();
            if (change is "terminal-clear") { preview.Clear(); }
            if (change is "terminal-control") { control = false; }
        };
        if (change is "release") { inspector.Refusal = "identity"; inspector.FailRelease = true; }
        (await Refresh(new(preview), file, source, () => control, TestContext.Current.CancellationToken)).Should().Be(change.StartsWith("terminal-", StringComparison.Ordinal)
            ? LocalFileOutcome.Cancelled : LocalFileOutcome.Unavailable);
        preview.Review.Should().BeNull();
        preview.FolderReview.Should().BeNull();
        preview.Current.Should().BeNull();
        preview.CurrentFolder.Should().BeNull();
        inspector.Reads.Should().Be(folder ? 2 : 1);
        if (change is "release")
        {
            preview.IsQuiescent.Should().BeFalse();
            await preview.Invoking(service => service.WaitForQuiescenceAsync()).Should().ThrowAsync<InvalidOperationException>();
            (await Refresh(new(preview), file, source, token: TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Denied);
        }
        else { await preview.WaitForQuiescenceAsync(); }
    }

    private static HostActivity Host() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);

    [Theory]
    [InlineData(false, "clear")]
    [InlineData(true, "clear")]
    [InlineData(false, "original-control")]
    [InlineData(true, "original-control")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "dispose")]
    [InlineData(true, "dispose")]
    [InlineData(false, "audit")]
    [InlineData(true, "audit")]
    [InlineData(false, "release")]
    [InlineData(true, "release")]
    public async Task ConfirmingRefreshedCandidateCannotPublishLateMixedUnauditedOrUnreleasedContent(bool folder, string change)
    {
        using var host = Host();
        var inspector = new Inspector();
        var audit = new Audit();
        using var preview = Preview(inspector, audit);
        var control = true;
        await Select(preview, folder, () => control);
        await Confirm(preview, folder, preview.Review?.ReviewId ?? preview.FolderReview!.ReviewId);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        (await Refresh(new(preview), file, source, token: TestContext.Current.CancellationToken)).Should().Be(LocalFileOutcome.Reviewed);
        var id = preview.Review?.ReviewId ?? preview.FolderReview!.ReviewId;
        inspector.CaptureGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var pending = folder ? preview.ConfirmFolderAsync(id, () => true, cancellation.Token) : preview.ConfirmAsync(id, () => true, cancellation.Token);
        await inspector.CaptureStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var wait = preview.WaitForQuiescenceAsync();
        wait.IsCompleted.Should().BeFalse();
        switch (change)
        {
            case "clear": preview.Clear(); break;
            case "dispose": preview.Dispose(); break;
            case "original-control": control = false; break;
            case "audit": audit.Reject = SecurityAuditOutcome.Succeeded; break;
            case "release": inspector.FailRelease = true; break;
            default: cancellation.Cancel(); break;
        }
        inspector.CaptureGate.SetResult();
        (await pending).Should().Be(change is "audit" or "release" ? LocalFileOutcome.Unavailable : LocalFileOutcome.Cancelled);
        preview.Current.Should().BeNull();
        preview.CurrentFolder.Should().BeNull();
        inspector.Buffers.SelectMany(bytes => bytes).Should().OnlyContain(value => value == 0);
        if (change is "release")
        {
            Func<Task> waitForRelease = () => wait;
            await waitForRelease.Should().ThrowAsync<InvalidOperationException>();
        }
        else { await wait; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshedReviewHasFreshDeadlineAndOriginalSessionTaskWithNoReusedConfirmation(bool folder)
    {
        using var host = Host();
        var inspector = new Inspector();
        var clock = new Clock();
        using var preview = new LocalFilePreview(inspector, new Audit(), clock, NullLogger<LocalFilePreview>.Instance);
        await Admit(preview, folder);
        var file = preview.Current;
        var source = preview.CurrentFolder;
        clock.Now = clock.Now.AddHours(1);
        await Refresh(new(preview), file, source, token: TestContext.Current.CancellationToken);
        var id = preview.Review?.ReviewId ?? preview.FolderReview!.ReviewId;
        foreach (var request in new[]
        {
            HostRequest.Create(RequestOrigin.LocalUi),
            new(new(Guid.NewGuid()), host.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            new(new(Guid.NewGuid()), host.Request.SessionId, host.Request.TaskId, RequestOrigin.ActivatedVoice),
        })
        {
            using var other = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            (await Confirm(preview, folder, id)).Should().Be(LocalFileOutcome.Stale);
        }
        clock.Now = clock.Now.AddMinutes(2);
        (await Confirm(preview, folder, id)).Should().Be(LocalFileOutcome.Stale);
        inspector.Reads.Should().Be(folder ? 2 : 1);
        preview.Review.Should().BeNull();
        preview.FolderReview.Should().BeNull();
        await preview.WaitForQuiescenceAsync();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static LocalFilePreview Preview(Inspector inspector, Audit audit) =>
        new(inspector, audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
    private static Task<LocalFileOutcome> Refresh(LocalFileRefresh refresh, LocalFileRevision? file, LocalFolderRevision? folder,
        Func<bool>? gate = null, CancellationToken token = default) => file is not null
            ? refresh.ExecuteAsync(file.Reference, gate ?? (() => true), token)
            : refresh.ExecuteAsync(folder!.Reference, gate ?? (() => true), token);
    private static Task<LocalFileOutcome> Select(LocalFilePreview preview, bool folder, Func<bool> gate) => folder
        ? preview.SelectFolderAsync(new Picker(), gate, TestContext.Current.CancellationToken)
        : preview.SelectAsync(new Picker(), gate, TestContext.Current.CancellationToken);
    private static Task<LocalFileOutcome> Confirm(LocalFilePreview preview, bool folder, Guid id) => folder
        ? preview.ConfirmFolderAsync(id, () => true, TestContext.Current.CancellationToken)
        : preview.ConfirmAsync(id, () => true, TestContext.Current.CancellationToken);
    private static async Task Admit(LocalFilePreview preview, bool folder)
    {
        (await Select(preview, folder, () => true)).Should().Be(LocalFileOutcome.Reviewed);
        (await Confirm(preview, folder, preview.Review?.ReviewId ?? preview.FolderReview!.ReviewId)).Should().Be(LocalFileOutcome.Admitted);
    }
    private sealed class Picker : IUserFilePicker, IUserFolderPicker
    {
        public Task<string?> SelectAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(@"C:\Team\guide.md");
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(@"C:\Team\Guides");
    }
    private sealed class Inspector : ILocalFileInspector
    {
        public string Text { get; set; } = "# Old\nprivate marker";
        public string[] Names { get; set; } = ["a.md", "b.md"];
        public string Refusal { get; set; } = "";
        public bool FailRelease { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource? CaptureGate { get; set; }
        public TaskCompletionSource CaptureStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; set; }
        public int Disposals { get; set; }
        public int Inspections { get; private set; }
        public List<string> Paths { get; } = [];
        public List<byte[]> Buffers { get; } = [];
        private async Task Inspect(string path)
        {
            Paths.Add(path);
            Inspections++;
            if (Gate is not null) { Started.TrySetResult(); await Gate.Task; }
            if (Refusal is "missing" or "subdirectory") { throw new IOException("Synthetic source unavailable."); }
        }
        public async Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken)
        {
            await Inspect(selectedPath);
            return new Selection(this, Metadata(selectedPath, "file"));
        }
        public async Task<ILocalFolderSelection> InspectFolderAsync(string selectedPath, CancellationToken cancellationToken)
        {
            await Inspect(selectedPath);
            return new FolderSelection(this, new LocalFolderMetadata(Refusal is "path" ? @"C:\Other\Guides" : selectedPath,
                Refusal is "identity" ? "replacement" : "directory",
                Names.Select(name => Metadata(selectedPath + "\\" + name, name))));
        }
        private LocalFileMetadata Metadata(string path, string identity) =>
            new(Refusal is "path" && identity is "file" ? @"C:\Team\alias.md" : path,
                Refusal is "identity" && identity is "file" ? "replacement" : identity,
                Refusal is "oversize" or "bounds" ? LocalFilePolicy.MaximumBytes + 1 : Encoding.UTF8.GetByteCount(Text),
                DateTimeOffset.UtcNow);
        public async Task<byte[]> ReadAsync()
        {
            Reads++;
            if (CaptureGate is not null) { CaptureStarted.TrySetResult(); await CaptureGate.Task; }
            var bytes = Encoding.UTF8.GetBytes(Text);
            Buffers.Add(bytes);
            return bytes;
        }
        public void Release()
        {
            Disposals++;
            if (FailRelease) { throw new IOException("Synthetic release unverified."); }
        }
    }
    private sealed class Selection(Inspector owner, LocalFileMetadata metadata) : ILocalFileSelection
    {
        public LocalFileMetadata Metadata => metadata;
        public Task<byte[]> ReadAsync(CancellationToken cancellationToken) => owner.ReadAsync();
        public void Dispose() => owner.Release();
    }
    private sealed class FolderSelection(Inspector owner, LocalFolderMetadata metadata) : ILocalFolderSelection
    {
        public LocalFolderMetadata Metadata => metadata;
        public Task ValidateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken) => owner.ReadAsync();
        public void Dispose() => owner.Release();
    }
    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public SecurityAuditOutcome? Reject { get; set; }
        public Action<SecurityAuditEvent>? Callback { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            Callback?.Invoke(auditEvent);
            if (auditEvent.Outcome == Reject) { throw new IOException("Synthetic audit unavailable."); }
            Events.Add(auditEvent);
        }
    }
}
