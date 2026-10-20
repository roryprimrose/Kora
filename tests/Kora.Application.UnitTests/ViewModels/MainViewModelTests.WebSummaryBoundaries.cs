using AwesomeAssertions;
using Kora.Application.Network;
using Kora.Application.UnitTests.Network;
using Kora.Application.ViewModels;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task WebSummaryWithoutComposedWorkflowReportsUnavailableAndKeepsSnapshot()
    {
        var fixture = WebFixture();
        await fixture.RunAsync("get web page https://example.com/");
        var source = fixture.ViewModel.WebResultDetailsReference;
        await fixture.ViewModel.PrepareWebSummaryAsync(source!.Value);
        fixture.ViewModel.WebSummaryStatus.Should().Contain("not composed");
        fixture.ViewModel.WebResultDetailsReference.Should().Be(source);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task WebSummaryOwnershipClosureBetweenSnapshotResolutionAndReviewCannotIssueConfirmation()
    {
        var fixture = WebFixture(new());
        await fixture.RunAsync("get web page https://example.com/");
        var reference = fixture.ViewModel.WebResultDetailsReference!.Value;
        var checks = 0;
        fixture.ViewModel.BindCallOwnershipGate(() => ++checks < 3);
        await fixture.ViewModel.PrepareWebSummaryAsync(reference);
        fixture.ViewModel.WebSummaryConfirmations.Should().BeEmpty();
        fixture.ViewModel.WebSummaryText.Should().BeEmpty();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task NativeWebSummaryAdmissionFailureIsExplicitAndDoesNotLeakExceptionText()
    {
        var logger = new EnabledLogger<MainViewModel>();
        var fixture = new Fixture(logger: logger);
        fixture.ViewModel.ReportWebSummaryFailure(new IOException("private source"));
        fixture.ViewModel.WebSummaryStatus.Should().Contain("native host admission failed");
        logger.Messages.Should().ContainSingle().Which.Should().NotContain("private source");
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("exit")]
    [InlineData("repeat")]
    public async Task WebSummaryActiveControlRetainsQuiescenceAndCancellation(string operation)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new WebSummaryExecution { Wait = async () => { entered.SetResult(); await release.Task; } };
        var fixture = WebFixture(execution);
        await fixture.RunAsync("get web page https://example.com/");
        await fixture.ViewModel.PrepareWebSummaryAsync(fixture.ViewModel.WebResultDetailsReference!.Value);
        var reference = fixture.ViewModel.WebResultDetailsReference!.Value;
        var pending = fixture.ViewModel.ConfirmWebSummaryAsync(fixture.ViewModel.WebSummaryConfirmations.Single().Id);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Task? exit = null;
        if (operation is "cancel") { await fixture.RunAsync("cancel local web summary"); }
        if (operation is "repeat")
        {
            await fixture.ViewModel.PrepareWebSummaryAsync(reference);
            fixture.ViewModel.WebSummaryStatus.Should().Contain("operation admission is closed");
        }
        if (operation is "exit")
        {
            exit = fixture.ViewModel.ExitAsync();
            exit.IsCompleted.Should().BeFalse();
        }
        release.SetResult();
        await pending;
        if (exit is not null) { await exit; }
        fixture.ViewModel.IsWebSummaryActive.Should().BeFalse();
        if (operation is not "repeat") { fixture.ViewModel.WebSummaryText.Should().BeEmpty(); }
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task CancelWebSummaryWithoutActiveExecutionIsExplicit()
    {
        var fixture = WebFixture(new());
        await fixture.RunAsync("cancel local web summary");
        fixture.ViewModel.WebSummaryStatus.Should().Contain("cancelled");
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("owner")]
    [InlineData("question")]
    [InlineData("busy")]
    [InlineData("fetch")]
    public async Task WebSummaryClosedEntryGatesNeverUseSnapshot(string closure)
    {
        var fixture = WebFixture(new());
        var fakeReference = new DetailContentReference(new(Guid.NewGuid()), 1);
        Task? pending = null;
        TaskCompletionSource? release = null;
        if (closure is "disposed") { fixture.ViewModel.Dispose(); }
        if (closure is "owner") { fixture.ViewModel.BindCallOwnershipGate(static () => false); }
        if (closure is "question")
        {
            fixture.Probe.Status = new("local.inference", "Ollama", DependencyReadiness.Ready, "Ready.");
            fixture.Reasoner.Question = new("Choose an option.", ["one", "two"]);
            await fixture.ViewModel.InitializeAsync();
            await fixture.RunAsync("help with this workstation");
            await fixture.ViewModel.ActiveReasoningTask!;
        }
        if (closure is "busy")
        {
            await fixture.ViewModel.InitializeAsync();
            fixture.Probe.Gate = release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = fixture.ViewModel.RefreshCommand.ExecuteAsync();
        }
        if (closure is "fetch")
        {
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new ViewModelWebTransport(WebResponse("https://example.com/", "source"))
            { BeforeSendAsync = () => release.Task };
            fixture.ViewModel.Dispose();
            fixture = new Fixture(webPageGet: new(transport, new ViewModelWebAudit()));
            pending = fixture.RunAsync("get web page https://example.com/");
        }
        await fixture.ViewModel.PrepareWebSummaryAsync(fakeReference);
        fixture.ViewModel.WebSummaryStatus.Should().Contain("operation admission is closed");
        release?.SetResult();
        if (pending is not null) { await pending; }
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("io")]
    [InlineData("argument")]
    [InlineData("io-retired")]
    [InlineData("credential")]
    public async Task WebSummaryFailuresDoNotPresentGeneratedSuccess(string fault)
    {
        Fixture? fixture = null;
        var execution = new WebSummaryExecution
        {
            Wait = () =>
            {
                if (fault is "io-retired") { fixture!.ViewModel.RetireWebResultDetails(); }
                throw fault switch
                {
                    "cancel" => new OperationCanceledException(),
                    "argument" => new ArgumentException("synthetic bad request"),
                    _ => new IOException("private source"),
                };
            },
        };
        fixture = WebFixture(execution, fault is "credential" ? "password=actual-secret-value" : "ordinary source");
        await fixture.RunAsync("get web page https://example.com/");
        await fixture.ViewModel.PrepareWebSummaryAsync(fixture.ViewModel.WebResultDetailsReference!.Value);
        if (fixture.ViewModel.WebSummaryConfirmations.Count > 0)
        { await fixture.ViewModel.ConfirmWebSummaryAsync(fixture.ViewModel.WebSummaryConfirmations.Single().Id); }
        fixture.ViewModel.WebSummaryText.Should().BeEmpty();
        if (fault is not ("cancel" or "io-retired")) { fixture.ViewModel.WebSummaryStatus.Should().Contain("refused"); }
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task WebSummaryReentrantPublicationRetiresAnswerInsteadOfRevivingClosedSource()
    {
        var fixture = WebFixture(new());
        await fixture.RunAsync("get web page https://example.com/");
        await fixture.ViewModel.PrepareWebSummaryAsync(fixture.ViewModel.WebResultDetailsReference!.Value);
        fixture.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.WebSummaryText) && fixture.ViewModel.WebSummaryText.Length > 0)
            { fixture.ViewModel.RetireWebResultDetails(); }
        };
        await fixture.ViewModel.ConfirmWebSummaryAsync(fixture.ViewModel.WebSummaryConfirmations.Single().Id);
        fixture.ViewModel.WebSummaryText.Should().BeEmpty();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task UnavailableAssistantPrefixCannotDispatchSummary()
    {
        var fixture = WebFixture(new());
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("Kora, summarize current web result");
        fixture.ViewModel.ResponseBody.Should().Contain("prefix routing is unavailable");
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReviewedWebSummaryContributesOnlyItsExactConfirmationToActivationGrammar()
    {
        var fixture = WebFixture(new());
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        await fixture.RunAsync("get web page https://example.com/");
        await fixture.ViewModel.PrepareWebSummaryAsync(fixture.ViewModel.WebResultDetailsReference!.Value);
        var phrase = WebSummaryCommand.ConfirmPrefix + fixture.ViewModel.WebSummaryConfirmations.Single().Id.ToString("D");
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.Voice.StartedPhrases.Should().Contain(phrase);
        fixture.ViewModel.HasWebResultDetails.Should().BeTrue();
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task ActivatedVoiceSummaryAndConfirmationPreserveTheExactFetchedSourceWithoutRefetch()
    {
        var execution = new WebSummaryExecution();
        var transport = new ViewModelWebTransport(WebResponse("https://example.com/", "ordinary source"));
        var fixture = new Fixture(webPageGet: new(transport, new ViewModelWebAudit()),
            webSummary: new(new(), execution, new ViewModelWebAudit(), TimeProvider.System, NullLogger<WebSummaryWorkflow>.Instance));
        fixture.Voice.Microphones = [new("0", "Headset")];
        fixture.Voice.DefaultMicrophoneId = "0";
        await fixture.ViewModel.InitializeAsync();
        if (!fixture.ViewModel.IsVoiceEnabled) { await fixture.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        await fixture.RunAsync("get web page https://example.com/");
        var reference = fixture.ViewModel.WebResultDetailsReference;
        await fixture.ViewModel.BeginPushToTalkAsync();
        fixture.ViewModel.WebResultDetailsReference.Should().Be(reference, "push-to-talk must preserve the original source");
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.ViewModel.WebResultDetailsReference.Should().Be(reference, "release must preserve the original source");
        await fixture.Voice.RaiseTranscriptAsync("Kora, summarize current web result", 1);
        fixture.ViewModel.WebSummaryConfirmations.Should().ContainSingle(
            "voice request status: {0}; response: {1} / {2}", fixture.ViewModel.WebSummaryStatus,
            fixture.ViewModel.ResponseTitle, fixture.ViewModel.ResponseBody);
        var review = fixture.ViewModel.WebSummaryConfirmations.Single();
        fixture.ViewModel.WebResultDetailsReference.Should().Be(reference);
        execution.Calls.Should().Be(0);
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        await fixture.Voice.RaiseTranscriptAsync("Kora, " + WebSummaryCommand.ConfirmPrefix + review.Id.ToString("D"), 1);
        fixture.ViewModel.WebSummaryText.Should().Be("Actual admitted model answer.");
        fixture.ViewModel.WebResultDetailsReference.Should().Be(reference);
        execution.Calls.Should().Be(1);
        transport.SendCalls.Should().Be(1);
        fixture.ViewModel.Dispose();
    }

    [Fact]
    public async Task WebSummaryPreservesExplicitUnavailableModelResult()
    {
        var fixture = WebFixture(new WebSummaryExecution
        { Result = new(ModelTurnOutcome.Unavailable, ModelTurnReason.QualificationPending) });
        await fixture.RunAsync("get web page https://example.com/");
        await fixture.ViewModel.PrepareWebSummaryAsync(fixture.ViewModel.WebResultDetailsReference!.Value);
        await fixture.ViewModel.ConfirmWebSummaryAsync(fixture.ViewModel.WebSummaryConfirmations.Single().Id);
        fixture.ViewModel.WebSummaryStatus.Should().Contain("QualificationPending");
        fixture.ViewModel.WebSummaryText.Should().BeEmpty();
        fixture.ViewModel.Dispose();
    }

    private static Fixture WebFixture(WebSummaryExecution? execution = null, string text = "ordinary source") =>
        new(webPageGet: new(new ViewModelWebTransport(WebResponse("https://example.com/", text)), new ViewModelWebAudit()),
            webSummary: execution is null ? null : new(new(), execution, new ViewModelWebAudit(), TimeProvider.System,
                NullLogger<WebSummaryWorkflow>.Instance));
}
