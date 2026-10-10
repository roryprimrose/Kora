using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task WebRetrievalBlocksHandoffAndExitWaitsForActualCancelledTransportRelease()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ViewModelWebTransport(WebResponse("https://example.com/", "late text"))
        {
            BeforeSendAsync = async () => { entered.SetResult(); await released.Task; },
        };
        var fixture = new Fixture(webPageGet: new(transport, new ViewModelWebAudit()));
        var retrieval = fixture.RunAsync("get web page https://example.com/");
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.ViewModel.IsWebPageRetrievalActive.Should().BeTrue();
        (await fixture.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        var exit = fixture.ViewModel.ExitAsync();
        exit.IsCompleted.Should().BeFalse();
        fixture.WindowActions.Should().NotContain(Kora.Application.ViewModels.WindowAction.Close);
        released.SetResult();
        await retrieval;
        await exit;
        fixture.ViewModel.HasWebResultDetails.Should().BeFalse();
        fixture.ViewModel.IsWebPageRetrievalActive.Should().BeFalse();
        fixture.WindowActions.Should().Contain(Kora.Application.ViewModels.WindowAction.Close);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task DeferredWebRetirementCannotClearAnUnrelatedLaterResponseEvenWithIdenticalBody()
    {
        var fixture = new Fixture(webPageGet: new(new ViewModelWebTransport(WebResponse("https://example.com/", "exact text")),
            new ViewModelWebAudit()));
        await fixture.RunAsync("get web page https://example.com/");
        var originalBody = fixture.ViewModel.ResponseBody;
        fixture.Dispatcher.BeforePost = () => fixture.ViewModel.ReportHostInteractionFailure(originalBody);
        fixture.ViewModel.RetireWebResultDetails();
        fixture.ViewModel.ResponseTitle.Should().Be("Native question unavailable.");
        fixture.ViewModel.ResponseBody.Should().Be(originalBody);
        fixture.ViewModel.HasWebResultDetails.Should().BeFalse();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task WebDetailsRequireOriginalOwningHostBeforeAnyRetrieval()
    {
        var transport = new ViewModelWebTransport(WebResponse("https://example.com/", "text"));
        var fixture = new Fixture(webPageGet: new(transport, new ViewModelWebAudit()));
        fixture.ViewModel.BindCallOwnershipGate(() => false);
        await fixture.RunAsync("get web page https://example.com/");
        transport.ResolveCalls.Should().Be(0);
        transport.SendCalls.Should().Be(0);
        fixture.ViewModel.HasWebResultDetails.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().Contain("ownership");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task ExpiredOriginalHostRequestCannotIssueWebDetailsFromItsCompletedActivity()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ViewModelWebTransport(WebResponse("https://example.com/", "late text"));
        var fixture = new Fixture(webPageGet: new(transport, new ViewModelWebAudit()));
        using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        transport.BeforeSendAsync = async () => { entered.SetResult(); await released.Task; };
        var retrieval = fixture.ViewModel.ExecuteWebPageCommandAsync(new(new("https://example.com/")),
            Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        original.Dispose();
        released.SetResult();
        await retrieval;
        fixture.ViewModel.HasWebResultDetails.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().NotContain("late text");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReentrantResponseCloseAndRevealCannotMintALateNewSnapshot()
    {
        var fixture = new Fixture(webPageGet: new(new ViewModelWebTransport(WebResponse("https://example.com/", "exact text")),
            new ViewModelWebAudit()));
        var closed = false;
        fixture.ViewModel.WindowActionRequested += (_, action) =>
        {
            if (!closed && action == Kora.Application.ViewModels.WindowAction.Show && fixture.ViewModel.HasWebResultDetails)
            {
                closed = true;
                fixture.ViewModel.HideApplication();
                fixture.ViewModel.ShowApplication();
            }
        };
        await fixture.RunAsync("get web page https://example.com/");
        closed.Should().BeTrue();
        fixture.ViewModel.HasWebResultDetails.Should().BeFalse();
        fixture.ViewModel.ResponseBody.Should().NotContain("exact text");
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Exact 😀\n<script>inert</script> https://example.com/image")]
    public async Task WebDetailsCaptureOnlyActualSuccessWithOriginalHostRequestAndExactBody(string text)
    {
        var transport = new ViewModelWebTransport(WebResponse("https://example.com/", text));
        var fixture = new Fixture(webPageGet: new(transport, new ViewModelWebAudit()));
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        await fixture.ViewModel.ExecuteWebPageCommandAsync(new(new("https://example.com/")),
            Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        var reference = fixture.ViewModel.WebResultDetailsReference!.Value;
        var content = fixture.ViewModel.ResolveWebResultDetails(reference)!;
        fixture.ViewModel.HasWebResultDetails.Should().BeTrue();
        fixture.ViewModel.WebResultDetailActions.Should().ContainSingle().Which.Should().Be(reference);
        fixture.ViewModel.IsCancelTaskVisible.Should().BeTrue();
        content.WebResult!.Provenance.OriginalRequest.Should().BeSameAs(request);
        content.WebResult.Text.Should().Be(text);
        content.WebResult.Provenance.ReturnedTextSha256.Should()
            .Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        fixture.ViewModel.ResolveWebResultDetails(reference).Should().BeSameAs(content);
        fixture.ViewModel.ResolveWebResultDetails(new(reference.ItemId, 2)).Should().BeNull();
        fixture.ViewModel.ResolveWebResultDetails(new(new(Guid.NewGuid()), 1)).Should().BeNull();
        fixture.ViewModel.ReportWebResultDetailStatus("Opened exact native details.");
        fixture.ViewModel.WebResultDetailStatus.Should().Be("Opened exact native details.");
        fixture.ViewModel.ResponseTitle.Should().Be("Web page retrieved.");
        fixture.ViewModel.IsWebPageRetrievalActive.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.Dispose();
        fixture.ViewModel.ResolveWebResultDetails(reference).Should().BeNull();
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("response")]
    [InlineData("call")]
    [InlineData("close")]
    public async Task WebDetailsCannotReadRenderSearchOrCopyAfterOriginalSourceRetirement(string retirement)
    {
        var transport = new ViewModelWebTransport(WebResponse("https://example.com/", "exact private body"));
        var fixture = new Fixture(webPageGet: new(transport, new ViewModelWebAudit()));
        await fixture.RunAsync("get web page https://example.com/");
        var reference = fixture.ViewModel.WebResultDetailsReference!.Value;
        var content = fixture.ViewModel.ResolveWebResultDetails(reference)!;
        var state = new DetailViewerRegistry().Open(content, true,
            () => ReferenceEquals(fixture.ViewModel.ResolveWebResultDetails(reference), content));
        var generation = state.Generation;
        state.Search("private").Should().BeTrue();
        state.TryGetCopySource(true, false, out _).Should().BeFalse();
        state.TryGetCopySource(true, true, out var source).Should().BeTrue();
        source.Should().Be(content.Source);
        switch (retirement)
        {
            case "hide": fixture.ViewModel.HideApplication(); break;
            case "privacy": fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true); await fixture.ViewModel.PrivacyClosureTask; break;
            case "owner": fixture.ViewModel.BindCallOwnershipGate(() => false); break;
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
            case "dispose": fixture.ViewModel.Dispose(); break;
            case "response": await fixture.RunAsync("get web page"); break;
            case "call": fixture.CallState.SetState(Kora.Core.Communication.CallState.Active); await fixture.ViewModel.CallClosureTask; break;
            case "close": fixture.ViewModel.RetireWebResultDetails(); break;
        }
        fixture.ViewModel.ResolveWebResultDetails(reference).Should().BeNull();
        fixture.ViewModel.HasWebResultDetails.Should().BeFalse();
        fixture.ViewModel.WebResultDetailActions.Should().BeEmpty();
        state.Content.Should().BeNull();
        state.ActiveText.Should().BeEmpty();
        state.SearchQuery.Should().BeEmpty();
        state.Search("private").Should().BeFalse();
        state.SetSource(true);
        state.CompleteRender(generation, "late", "Rendered.", false).Should().BeFalse();
        state.TryGetCopySource(true, true, out _).Should().BeFalse();
        fixture.ViewModel.BindCallOwnershipGate(() => true);
        fixture.ViewModel.ResolveWebResultDetails(reference).Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().NotContain("exact private body");
        state.Content.Should().BeNull();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("response")]
    public async Task LateWebRetrievalCannotMintDetailsAfterControlOrSourceChanges(string retirement)
    {
        var transport = new ViewModelWebTransport(WebResponse("https://example.com/", "late body"));
        var fixture = new Fixture(webPageGet: new(transport, new ViewModelWebAudit()));
        transport.BeforeSendAsync = async () =>
        {
            switch (retirement)
            {
                case "hide": fixture.ViewModel.HideApplication(); break;
                case "privacy": fixture.ViewModel.CloseForObservedPrivacyEvent("locked", true); break;
                case "owner": fixture.ViewModel.BindCallOwnershipGate(() => false); break;
                case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
                case "dispose": fixture.ViewModel.Dispose(); break;
                case "response": fixture.ViewModel.ReportHostInteractionFailure("replacement"); break;
            }
        };
        await fixture.RunAsync("get web page https://example.com/");
        fixture.ViewModel.HasWebResultDetails.Should().BeFalse();
        fixture.ViewModel.WebResultDetailsReference.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().NotContain("late body");
        fixture.ViewModel.IsWebPageRetrievalActive.Should().BeFalse();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task PendingQuestionCannotBeReplacedByWebRetrievalOrDetailFeedback()
    {
        var fixture = new Fixture(webPageGet: new(new ViewModelWebTransport(WebResponse("https://example.com/", "text")),
            new ViewModelWebAudit()));
        fixture.Probe.Status = new("local.inference", "Ollama", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready.");
        fixture.Reasoner.Question = new("What should be done?", ["first", "second"]);
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("help with this workstation");
        await fixture.ViewModel.ActiveReasoningTask!;
        var body = fixture.ViewModel.ResponseBody;
        var choices = fixture.ViewModel.ModelQuestionChoices;
        await fixture.RunAsync("get web page https://example.com/");
        fixture.ViewModel.ReportWebResultDetailStatus("Unavailable.");
        fixture.ViewModel.IsModelQuestionPending.Should().BeTrue();
        fixture.ViewModel.ModelQuestionChoices.Should().BeSameAs(choices);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.HasWebResultDetails.Should().BeFalse();
        fixture.ViewModel.Dispose();
    }
}
