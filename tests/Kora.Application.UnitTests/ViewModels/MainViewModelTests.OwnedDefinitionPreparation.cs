using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Files;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("prepare owned definition prompt time")]
    [InlineData("prepare owned definition instruction date")]
    [InlineData("owned definition help")]
    public async Task OwnedPreparationTakesPrecedenceOverClockShapedAssistantAlias(string command)
    {
        var f = new Fixture();
        var native = new OwnedPreparationPresenter();
        f.ViewModel.BindOwnedDefinitionPreparation(native);
        await f.ViewModel.SetAssistantNameAsync(command[..command.LastIndexOf(' ')]);

        await f.RunAsync(command);

        f.ViewModel.ResponseTitle.Should().NotBe("Host-local date and time.");
        if (string.Equals(command, "owned definition help", StringComparison.Ordinal))
        {
            f.ViewModel.ResponseTitle.Should().Contain("help");
            native.Captures.Should().Be(0);
        }
        else
        {
            native.Calls.Should().Be(1);
        }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, ArtifactKind.Instruction)]
    [InlineData(false, ArtifactKind.Prompt)]
    [InlineData(true, ArtifactKind.Instruction)]
    [InlineData(true, ArtifactKind.Prompt)]
    public async Task OwnedPreparationIsOriginalBoundPrivateAndRequiresNativeAcknowledgement(bool voice, ArtifactKind kind)
    {
        var logger = new MemoryPrivacyLogger();
        var f = new Fixture(logger: logger);
        var native = new OwnedPreparationPresenter();
        f.ViewModel.BindOwnedDefinitionPreparation(native);
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, prepare", 1); }
        native.Prepare = (actualKind, id, admitted) =>
        {
            actualKind.Should().Be(kind);
            id.Should().Be("private-owned-id");
            admitted().Should().BeTrue();
            HostActivity.RequireCurrent().Request.Origin.Should().Be(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
            return true;
        };
        var command = "prepare owned definition " + kind.ToString().ToLowerInvariant() + " private-owned-id";
        if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + command, 1); }
        else { await f.RunAsync(command); }
        native.Calls.Should().Be(1);
        f.ViewModel.ResponseTitle.Should().Contain("PREPARED");
        f.ViewModel.ResponseBody.Should().Contain("AND-composed").And.NotContain("private-owned-id");
        f.ViewModel.Transcript.Should().NotContain("private-owned-id");
        logger.Messages.Should().NotContain(message => message.Contains("private-owned-id", StringComparison.Ordinal));
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
        native.Admitted!().Should().BeFalse();
        await f.RunAsync("owned definition help");
        native.Admitted().Should().BeFalse();
        native.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("no-consumer")]
    [InlineData("no-ack")]
    [InlineData("native-lifetime")]
    [InlineData("exception")]
    [InlineData("capture-exception")]
    [InlineData("system")]
    [InlineData("call-owner")]
    [InlineData("voice-owner")]
    [InlineData("unprefixed-voice")]
    public async Task OwnedPreparationFailsClosedWithoutFallbackOrSuccess(string state)
    {
        var f = new Fixture();
        var native = new OwnedPreparationPresenter { Current = state is not "native-lifetime" };
        if (state is "capture-exception") { native.CaptureException = new IOException("private-owned-id"); }
        native.Prepare = (_, _, _) => state is "exception" ? throw new IOException("private-owned-id") : state is not "no-ack";
        if (state is not "no-consumer") { f.ViewModel.BindOwnedDefinitionPreparation(native); }
        if (state is "call-owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
        const string command = "prepare owned definition prompt private-owned-id";
        if (state is "system")
        {
            using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Application, HostOperation.Request);
            await f.RunAsync(command);
        }
        else if (state is "voice-owner" or "unprefixed-voice")
        {
            await f.RaiseActivatedTranscriptAsync("Kora, prepare", 1);
            if (state is "voice-owner") { f.ViewModel.BindVoiceOwnershipGate(() => false); }
            await f.RaiseActivatedTranscriptAsync((state is "voice-owner" ? "Kora, " : string.Empty) + command, 1);
        }
        else { await f.RunAsync(command); }
        f.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        f.ViewModel.ResponseBody.Should().NotContain("private-owned-id");
        f.ViewModel.Transcript.Should().NotContain("private-owned-id");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("input")]
    [InlineData("input-aba")]
    [InlineData("new-command")]
    [InlineData("rebind")]
    [InlineData("owner")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("name")]
    [InlineData("lock")]
    [InlineData("dispose")]
    [InlineData("native")]
    public async Task OwnedPreparationBindsQueuedNativeOpenToOriginalHostInputAndLifetime(string change)
    {
        var f = new Fixture();
        var native = new OwnedPreparationPresenter();
        f.ViewModel.BindOwnedDefinitionPreparation(native);
        f.Dispatcher.DeferPosts = true;
        const string command = "prepare owned definition instruction private-owned-id";
        await f.RunAsync(command);
        switch (change)
        {
            case "input": f.ViewModel.CommandText = "changed"; break;
            case "input-aba": f.ViewModel.CommandText = "changed"; f.ViewModel.CommandText = command; break;
            case "new-command": await f.RunAsync("owned definition help"); break;
            case "rebind": f.ViewModel.BindOwnedDefinitionPreparation(new OwnedPreparationPresenter()); break;
            case "owner": f.ViewModel.BindCallOwnershipGate(() => true); break;
            case "privacy": f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 }; break;
            case "call": await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, f.ViewModel.CallPolicyRevision,
                TestContext.Current.CancellationToken); break;
            case "name": await f.ViewModel.SetAssistantNameAsync("Nova"); break;
            case "lock": f.Session.IsUnlocked = false; break;
            case "dispose": f.ViewModel.Dispose(); break;
            case "native": native.Current = false; break;
        }
        f.Dispatcher.FlushPosts();
        native.Calls.Should().Be(0);
        f.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("owned definition help")]
    [InlineData("prepare owned definition instruction example")]
    [InlineData("prepare owned definition Instruction example")]
    [InlineData("owned definition unrecognized private-owned-id")]
    public async Task OwnedGrammarNeverAnswersOrReplacesPendingQuestion(string command)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Exact pending question?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("Explain the current situation");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        var body = f.ViewModel.ResponseBody;
        var requests = f.Reasoner.Requests.Count;
        var native = new OwnedPreparationPresenter();
        f.ViewModel.BindOwnedDefinitionPreparation(native);
        await f.RunAsync(command);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Count.Should().Be(requests);
        native.Calls.Should().Be(0);
    }

    [Fact]
    public async Task OwnedHelpAndMalformedInputReadNothingAndPostNoNativeHandoff()
    {
        var f = new Fixture();
        var native = new OwnedPreparationPresenter();
        f.ViewModel.BindOwnedDefinitionPreparation(native);
        await f.RunAsync("owned definition help");
        f.ViewModel.ResponseTitle.Should().Contain("help");
        native.Captures.Should().Be(0);
        await f.RunAsync("prepare owned definition prompt ../private-owned-id");
        f.ViewModel.ResponseTitle.Should().Contain("unavailable");
        native.Captures.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NativeAcknowledgementCannotOutliveItsOwnAdmission()
    {
        var f = new Fixture();
        var native = new OwnedPreparationPresenter();
        native.Prepare = (_, _, _) => { f.ViewModel.CommandText = "changed"; return true; };
        f.ViewModel.BindOwnedDefinitionPreparation(native);
        await f.RunAsync("prepare owned definition instruction example");
        f.ViewModel.ResponseTitle.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedPreparationDoesNotReplaceOrConfirmPendingPrivateSourceReview(bool folder)
    {
        var f = new Fixture();
        using var preview = new LocalFilePreview(folder ? new FolderInspector() : new FakeFileInspector(),
            f.Audit, TimeProvider.System, NullLogger<LocalFilePreview>.Instance);
        f.ViewModel.BindFilePreview(preview, new FakeFilePicker(), folders: new NativeFolderPicker());
        if (folder) { await f.ViewModel.PreviewFolderAsync(); }
        else { await f.ViewModel.PreviewFileAsync(); }
        var fileReview = f.ViewModel.FileReview;
        var folderReview = f.ViewModel.FolderReview;
        var body = f.ViewModel.ResponseBody;
        var native = new OwnedPreparationPresenter();
        f.ViewModel.BindOwnedDefinitionPreparation(native);
        await f.RunAsync("prepare owned definition instruction example");
        f.ViewModel.FileReview.Should().BeSameAs(fileReview);
        f.ViewModel.FolderReview.Should().BeSameAs(folderReview);
        f.ViewModel.ResponseBody.Should().Be(body);
        native.Captures.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedPreparationDoesNotReplaceGrantEditorOrConfirmSecurityPreview(bool pendingApproval)
    {
        var f = new Fixture();
        await f.ViewModel.PrepareGrantChangeAsync(new(pendingApproval ? Kora.Core.Commands.GrantChangeOperation.Add : Kora.Core.Commands.GrantChangeOperation.Remove,
            Kora.Core.Commands.BuiltInAction.LockMachine, Kora.Core.Commands.ModelApprovalScope.Always));
        (pendingApproval ? f.ViewModel.IsGrantChangePending : f.ViewModel.IsGrantEditorVisible).Should().BeTrue();
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("owned definition help");
        f.ViewModel.ResponseBody.Should().Be(body);
        (pendingApproval ? f.ViewModel.IsGrantChangePending : f.ViewModel.IsGrantEditorVisible).Should().BeTrue();
    }

    [Fact]
    public async Task OwnedPreparationLeavesExactProviderSecurityPreviewUnchanged()
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local,
            [Kora.Application.UnitTests.Configuration.ProviderProfileFieldConfigurationTests.BaseTuple()]);
        await f.ViewModel.InitializeAsync();
        var id = Kora.Application.UnitTests.Configuration.ProviderProfileFieldConfigurationTests.Replacement(
            Kora.Core.Providers.ProviderProfileField.Reasoning);
        await f.RunAsync("set providers.app.reasoning to " + id + " for account at 1");
        var confirmation = f.ViewModel.ProviderFieldConfirmationId;
        confirmation.Should().NotBeNull();
        var body = f.ViewModel.ResponseBody;
        var preview = f.ViewModel.ProviderFieldPreviewStatus;
        await f.RunAsync("prepare owned definition instruction example");
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ViewModel.ProviderFieldConfirmationId.Should().Be(confirmation);
        f.ViewModel.ProviderFieldPreviewStatus.Should().Be(preview);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("consent")]
    [InlineData("owner")]
    [InlineData("voice")]
    public async Task QueuedOwnedVoicePreparationCannotSurviveVoiceConsentOwnerOrGenerationRetirement(string change)
    {
        var f = new Fixture();
        await f.RaiseActivatedTranscriptAsync("Kora, prepare", 1);
        var native = new OwnedPreparationPresenter();
        f.ViewModel.BindOwnedDefinitionPreparation(native);
        f.Dispatcher.DeferPosts = true;
        await f.RaiseActivatedTranscriptAsync("Kora, prepare owned definition prompt private-owned-id", 1);
        if (change is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
        else if (change is "owner") { f.ViewModel.BindVoiceOwnershipGate(() => false); }
        else { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        f.Dispatcher.FlushPosts();
        native.Calls.Should().Be(0);
        f.ViewModel.ResponseTitle.Should().NotContain("PREPARED");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    private sealed class OwnedPreparationPresenter : IOwnedDefinitionPreparation
    {
        internal bool Current = true;
        internal int Calls;
        internal int Captures;
        internal Exception? CaptureException;
        internal Func<ArtifactKind, string, Func<bool>, bool> Prepare = (_, _, _) => true;
        internal Func<bool>? Admitted;
        public Func<bool> CaptureLifetime()
        {
            Captures++;
            if (CaptureException is { } failure) { throw failure; }
            return () => Current;
        }
        public bool TryPrepare(ArtifactKind kind, string ownedId, Func<bool> admitted)
        { Calls++; Admitted = admitted; return Prepare(kind, ownedId, admitted); }
    }
}
