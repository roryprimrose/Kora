using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PreparedLocalSearchPreservesOriginalQueryAndExactSourceWithoutExecutingAnything(bool folder, bool voice)
    {
        var logger = new MemoryPrivacyLogger();
        var fixture = new Fixture(logger: logger);
        var files = new FakeFileInspector();
        var folders = new FolderInspector();
        var engine = new PreparedSearchRetrieval();
        using var preview = new LocalFilePreview(folder ? folders : files, fixture.Audit,
            TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker(),
            new(preview, engine, fixture.Audit, TimeProvider.System, NullLogger<LocalFileSearch>.Instance),
            new NativeFolderPicker());
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, prepare", 1); }
        await AdmitPreparedSource(fixture, folder);
        var file = fixture.ViewModel.FileRevision;
        var complete = fixture.ViewModel.FolderRevision;
        var auditCount = fixture.Audit.Events.Count;
        const string literal = "  PrIvAtE e\u0301界🙂 + \"Quoted\" -- path\\no-action  ";
        var command = $"prepare {(folder ? "folder" : "file")} search for \"{literal.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        var callbacks = 0;
        PreparedLocalSearchEventArgs? retired = null;
        fixture.ViewModel.LocalSearchPreparationRequested += (_, request) =>
        {
            callbacks++;
            retired = request;
            request.IsCurrent.Should().BeTrue();
            request.File.Should().Be(file?.Reference);
            request.Folder.Should().Be(complete?.Reference);
            HostActivity.RequireCurrent().Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            request.TryPrepare(query =>
            {
                query.Should().Be(literal);
                return true;
            }).Should().BeTrue();
            request.TryPrepare(_ => throw new InvalidOperationException("No reuse")).Should().BeFalse();
        };
        if (voice) { await fixture.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
        else { await fixture.RunAsync(command); }
        callbacks.Should().Be(1);
        retired!.IsCurrent.Should().BeFalse();
        retired.TryPrepare(_ => throw new InvalidOperationException("No late callbacks")).Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Contain("PREPARED / REVIEW NEEDED");
        fixture.ViewModel.ResponseBody.Should().Contain("No search has run").And.NotContain(literal);
        fixture.ViewModel.Transcript.Should().NotContain(literal);
        files.Reads.Should().Be(folder ? 0 : 1);
        folders.Reads.Should().Be(folder ? 2 : 0);
        engine.Calls.Should().Be(0);
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ClipboardReader.Calls.Should().Be(0);
        fixture.Audit.Events.Skip(auditCount).Should().NotContain(item =>
            item.ActionId.Contains("search", StringComparison.Ordinal));
        logger.Messages.Should().NotContain(message => message.Contains(literal, StringComparison.Ordinal));
        using (var activated = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request))
        {
            var denied = folder ? await fixture.ViewModel.SearchFolderAsync(complete!.Reference, literal)
                : await fixture.ViewModel.SearchFileAsync(file!.Reference, literal);
            denied.Outcome.Should().Be(LocalFileSearchOutcome.Stale);
            engine.Calls.Should().Be(0);
        }
        var result = folder ? await fixture.ViewModel.SearchFolderAsync(complete!.Reference, literal)
            : await fixture.ViewModel.SearchFileAsync(file!.Reference, literal);
        result.Outcome.Should().Be(LocalFileSearchOutcome.Matched);
        engine.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("prepare file search for \"")]
    [InlineData("prepare file search for \"private query\" tail")]
    [InlineData("prepare file search for \"private\u202equery\"")]
    public async Task ReservedPreparationNeverFallsIntoQuestionApprovalModelOrTranscriptRouting(string command)
    {
        var fixture = new Fixture();
        await fixture.RunAsync(command);
        fixture.ViewModel.ResponseTitle.Should().Contain("refused");
        fixture.ViewModel.Transcript.Should().NotContain("private");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.ClipboardReader.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("review", false)]
    [InlineData("folder", false)]
    [InlineData("file", true)]
    [InlineData("no-window", false)]
    [InlineData("no-consumer", false)]
    [InlineData("system", false)]
    [InlineData("unprefixed-voice", false)]
    [InlineData("voice-owner", false)]
    [InlineData("denied-window", false)]
    public async Task PreparationRefusesUnknownWrongKindUnconfirmedUnavailableAndNonoriginalAuthority(string state, bool folder)
    {
        var fixture = new Fixture();
        using var preview = new LocalFilePreview(new FakeFileInspector(), fixture.Audit,
            TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker());
        if (state is "unprefixed-voice" or "voice-owner")
        {
            await fixture.RaiseActivatedTranscriptAsync("Kora, prepare", 1);
        }
        if (state is not "none")
        {
            await fixture.ViewModel.PreviewFileAsync();
            if (state is not "review") { await fixture.ViewModel.ConfirmFilePreviewAsync(fixture.ViewModel.FileReview!.ReviewId); }
        }
        var calls = 0;
        if (state is not "no-consumer")
        {
            fixture.ViewModel.LocalSearchPreparationRequested += (_, request) =>
            {
                calls++;
                if (state is not "no-window") { request.TryPrepare(_ => state is not "denied-window"); }
            };
        }
        if (state is "voice-owner") { fixture.ViewModel.BindVoiceOwnershipGate(() => false); }
        var command = $"prepare {(folder || state is "folder" ? "folder" : "file")} search for \"private\"";
        if (state is "system")
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
            await fixture.RunAsync(command);
        }
        else if (state is "unprefixed-voice" or "voice-owner")
        {
            await fixture.RaiseActivatedTranscriptAsync((state is "voice-owner" ? "Kora, " : string.Empty) + command, 1);
        }
        else { await fixture.RunAsync(command); }
        calls.Should().Be(state is "no-window" or "denied-window" ? 1 : 0);
        fixture.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("source", false)]
    [InlineData("source", true)]
    [InlineData("replace", false)]
    [InlineData("replace", true)]
    [InlineData("cancel", false)]
    [InlineData("owner", false)]
    [InlineData("call", false)]
    [InlineData("lock", false)]
    [InlineData("privacy", false)]
    [InlineData("name", false)]
    [InlineData("input", false)]
    [InlineData("dispose", false)]
    public async Task QueuedPreparationCannotSubstituteNewerSourceOrSurviveAdmissionChanges(string change, bool folder)
    {
        var fixture = new Fixture();
        using var preview = new LocalFilePreview(folder ? new FolderInspector() : new FakeFileInspector(),
            fixture.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        await AdmitPreparedSource(fixture, folder);
        var callbacks = 0;
        fixture.ViewModel.LocalSearchPreparationRequested += (_, request) => { callbacks++; request.TryPrepare(_ => true); };
        fixture.Dispatcher.DeferPosts = true;
        await fixture.RunAsync($"prepare {(folder ? "folder" : "file")} search for \"private\"");
        switch (change)
        {
            case "source": fixture.ViewModel.ClearFilePreview(); break;
            case "replace": await AdmitPreparedSource(fixture, folder); break;
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
            case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "call": await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                fixture.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken); break;
            case "lock": fixture.Session.IsUnlocked = false; break;
            case "privacy": fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with
                { TopologyRevision = fixture.PrivacyObservation.Current.TopologyRevision + 1 }; break;
            case "name": await fixture.ViewModel.SetAssistantNameAsync("Nova"); break;
            case "input": await fixture.RunAsync("prepare"); break;
            default: fixture.ViewModel.Dispose(); break;
        }
        fixture.Dispatcher.FlushPosts();
        callbacks.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("before")]
    [InlineData("inside")]
    [InlineData("unused")]
    [InlineData("after")]
    public async Task ScopedNativeHandoffRechecksEligibilityAndExpiresEvenIfConsumerKeepsIt(string boundary)
    {
        var fixture = new Fixture();
        using var preview = new LocalFilePreview(new FakeFileInspector(), fixture.Audit,
            TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker());
        await AdmitPreparedSource(fixture, false);
        PreparedLocalSearchEventArgs? captured = null;
        fixture.ViewModel.LocalSearchPreparationRequested += (_, request) =>
        {
            captured = request;
            if (boundary is "unused") { return; }
            if (boundary is "before") { fixture.ViewModel.ClearFilePreview(); }
            if (boundary is "after")
            {
                request.TryPrepare(_ => true).Should().BeTrue();
                fixture.ViewModel.ClearFilePreview();
                return;
            }
            request.TryPrepare(_ =>
            {
                fixture.ViewModel.ClearFilePreview();
                return true;
            }).Should().BeFalse();
        };
        await fixture.RunAsync("prepare file search for \"private\"");
        captured!.IsCurrent.Should().BeFalse();
        captured.TryPrepare(_ => throw new InvalidOperationException("Late callback")).Should().BeFalse();
        fixture.ViewModel.ResponseTitle.Should().Contain("refused");
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("owner")]
    [InlineData("voice")]
    public async Task QueuedActivatedPreparationRequiresCurrentConsentVoiceLifetimeAndOwner(string change)
    {
        var fixture = new Fixture();
        await fixture.RaiseActivatedTranscriptAsync("Kora, prepare", 1);
        using var preview = new LocalFilePreview(new FakeFileInspector(), fixture.Audit,
            TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        fixture.ViewModel.BindFilePreview(preview, new FakeFilePicker());
        await AdmitPreparedSource(fixture, false);
        fixture.Dispatcher.DeferPosts = true;
        await fixture.RaiseActivatedTranscriptAsync("Kora, prepare file search for \"private\"", 1);
        var callbacks = 0;
        fixture.ViewModel.LocalSearchPreparationRequested += (_, request) => { callbacks++; request.TryPrepare(_ => true); };
        if (change is "consent") { await fixture.ViewModel.SetVoiceConsentAsync(false); }
        else if (change is "owner") { fixture.ViewModel.BindVoiceOwnershipGate(() => false); }
        else { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        fixture.Dispatcher.FlushPosts();
        callbacks.Should().Be(0);
        fixture.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ReservedPreparationLeavesPendingQuestionIntactWithoutSupplyingItsPrivateLiteral()
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        fixture.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("choose");
        await fixture.ViewModel.ActiveReasoningTask!;
        var calls = fixture.Reasoner.Requests.Count;
        await fixture.RunAsync("prepare file search for \"One private query\"");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Contain("refused");
        fixture.Reasoner.Requests.Should().HaveCount(calls);
        fixture.ViewModel.Transcript.Should().NotContain("One private query");
    }

    private static async Task AdmitPreparedSource(Fixture fixture, bool folder)
    {
        if (folder) { await fixture.ViewModel.PreviewFolderAsync(); }
        else { await fixture.ViewModel.PreviewFileAsync(); }
        await fixture.ViewModel.ConfirmFilePreviewAsync(fixture.ViewModel.FileReview?.ReviewId ?? fixture.ViewModel.FolderReview!.ReviewId);
    }

    private sealed class PreparedSearchRetrieval : ILocalFileRetrieval
    {
        public int Calls { get; private set; }
        public LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource, string query,
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            Calls++;
            return new LocalFileLexicalRetrieval().Search(revision, exactSource, query, observedAt, cancellationToken);
        }
        public LocalFileSearchResult Search(LocalFolderRevision revision, LocalFolderReference exactSource, string query,
            DateTimeOffset observedAt, CancellationToken cancellationToken)
        {
            Calls++;
            return new LocalFileLexicalRetrieval().Search(revision, exactSource, query, observedAt, cancellationToken);
        }
    }
}
