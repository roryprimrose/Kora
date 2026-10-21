using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Core;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clipboard_status_requires_current_name_for_voice_without_forced_speech(bool activated)
    {
        var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.AssistantNameInput = "Nova Prime";
        await fixture.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        await fixture.ViewModel.PreviewClipboardAsync();
        var id = fixture.ViewModel.ClipboardPreview!.SnapshotId;
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RaiseActivatedTranscriptAsync((activated ? "Nova Prime, " : "")
            + "clipboard snapshot status " + id.ToString("D"), 1);
        fixture.ViewModel.Transcript.Should().StartWith(activated ? "Clipboard snapshot present:" : "clipboard snapshot status");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ClipboardReader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Closed_host_does_not_publish_syntax_or_status()
    {
        var fixture = new Fixture();
        var transcript = fixture.ViewModel.Transcript;
        fixture.Session.IsUnlocked = false;
        await fixture.RunAsync("clipboard snapshot help");
        fixture.ViewModel.Transcript.Should().Be(transcript);
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.ShowClipboardSnapshotStatusAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        fixture.ViewModel.Transcript.Should().Be(transcript);
    }

    [Fact]
    public async Task Native_status_preserves_original_host_origin_and_rejects_nonhuman_context()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        await fixture.ViewModel.ShowClipboardSnapshotStatusAsync(fixture.ViewModel.ClipboardPreview!.SnapshotId,
            TestContext.Current.CancellationToken);
        fixture.ViewModel.Transcript.Should().Contain("snapshot unavailable");
        fixture.ClipboardReader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Exact_snapshot_status_and_help_are_content_free_transcript_only()
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "secret body never disclose";
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("clipboard snapshot status " + snapshot.SnapshotId.ToString("D"));
        fixture.ViewModel.Transcript.Should().Be($"Clipboard snapshot present: {snapshot.SnapshotId:D}; {snapshot.Utf8Bytes} UTF-8 bytes.");
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.ClipboardPreview.Should().BeSameAs(snapshot);
        await fixture.RunAsync("Kora, clipboard snapshot help");
        fixture.ViewModel.Transcript.Should().NotContain(snapshot.SnapshotId.ToString("D"));
        fixture.ViewModel.Transcript.Should().NotContain(fixture.ClipboardReader.Text);
        fixture.ClipboardReader.Calls.Should().Be(1);
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Audit.Events.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("clipboard snapshot status invalid")]
    [InlineData("clipboard snapshot help extra")]
    [InlineData("clipboard\tsnapshot status private-marker")]
    public async Task Reserved_malformed_commands_never_reach_question_or_selector(string command)
    {
        var fixture = new Fixture();
        await fixture.RunAsync(command);
        fixture.ViewModel.Transcript.Should().Contain("exact nonempty lowercase");
        fixture.ViewModel.Transcript.Should().NotContain("private-marker");
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ClipboardReader.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("different")]
    [InlineData("revoked")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("cancel")]
    public async Task Status_is_unavailable_without_exact_live_source_and_does_not_read(string change)
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        var id = fixture.ViewModel.ClipboardPreview!.SnapshotId;
        switch (change)
        {
            case "different": id = Guid.NewGuid(); break;
            case "revoked": fixture.ViewModel.ClearClipboardPreview(); break;
            case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "call":
                await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                    fixture.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken);
                break;
        }
        await fixture.ViewModel.ShowClipboardSnapshotStatusAsync(id,
            change is "cancel" ? new CancellationToken(true) : TestContext.Current.CancellationToken);
        fixture.ViewModel.Transcript.Should().Contain("snapshot unavailable");
        fixture.ViewModel.Transcript.Should().NotContain(id.ToString("D"));
        fixture.ClipboardReader.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Help_and_status_preserve_pending_question_or_action_approval_and_response(bool approval)
    {
        var fixture = new Fixture();
        fixture.Probe.Status = new DependencyStatus("local.inference", "Local model inference (Ollama)",
            DependencyReadiness.Ready, "Inference verified.");
        if (approval) { fixture.Reasoner.Action = BuiltInAction.LockMachine; }
        else { fixture.Reasoner.Question = new LocalModelQuestion("Which target?", ["Current", "Other"]); }
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("/lock");
        await fixture.ViewModel.ActiveReasoningTask!;
        fixture.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        fixture.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        var body = fixture.ViewModel.ResponseBody;
        var requests = fixture.Reasoner.Requests.Count;
        await fixture.RunAsync("clipboard snapshot help");
        await fixture.RunAsync("clipboard snapshot status invalid");
        await fixture.RunAsync("clipboard snapshot status 01234567-89ab-cdef-0123-456789abcdef");
        fixture.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        fixture.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.Reasoner.Requests.Should().HaveCount(requests);
    }

    [Fact]
    public async Task Status_preserves_pending_grant_preview()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        await fixture.ViewModel.PrepareGrantChangeAsync(new GrantChange(
            GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("clipboard snapshot help");
        fixture.ViewModel.IsGrantChangePending.Should().BeTrue();
        fixture.ViewModel.ResponseBody.Should().Be(body);
    }
}
