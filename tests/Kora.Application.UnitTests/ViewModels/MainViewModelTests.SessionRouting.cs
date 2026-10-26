using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Storage;
using Kora.Application.Hosting.SessionRouting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task UntargetedOriginalRequestsUseRoutingButExactNewNameAndBuiltinsDoNot()
    {
        var f = await Fixture.CreateInitializedAsync();
        var bodies = new List<string>();
        f.ViewModel.BindSessionRouting((body, origin, admitted) =>
        {
            origin.Should().Be(RequestOrigin.LocalUi);
            admitted().Should().BeTrue();
            bodies.Add(body);
            return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
        });
        await f.RunAsync("  Preserve this exact request  ");
        bodies.Should().Equal("  Preserve this exact request  ");
        await f.RunAsync("open new request");
        await f.RunAsync("session request review \"Alpha\" -- exact addressed body");
        await f.RunAsync("what version are you running");
        bodies.Should().ContainSingle();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ActivatedVoiceKeepsOriginalOriginAndNeverCleansesThroughNativeRouting()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.ViewModel.SetAssistantNameAsync("Nova");
        var origins = new List<RequestOrigin>();
        f.ViewModel.BindSessionRouting((body, origin, admitted) =>
        {
            admitted().Should().BeTrue();
            body.Should().Be("compare these plans");
            origins.Add(origin);
            return Task.FromResult<Kora.Application.Hosting.SessionRouting.SessionRoutingReceipt?>(null);
        });
        await f.RaiseActivatedTranscriptAsync("Nova, compare these plans", 1);
        origins.Should().Equal(RequestOrigin.ActivatedVoice);
        await f.RaiseActivatedTranscriptAsync("compare these plans", 1);
        origins.Should().ContainSingle();
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task RoutingFailureDoesNotFallBackToLegacyModelOrSelectAnotherConversation()
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionRouting((_, _, _) => throw new InvalidOperationException("stale exact descriptor"));
        await f.RunAsync("compare these plans");
        f.ViewModel.ResponseTitle.Should().Be("Session routing stopped.");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulRoutingReportsTheActualDestinationAndUnavailableHostOutcome(bool unnamed)
    {
        var f = await Fixture.CreateInitializedAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var target = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), new(request.SessionId, new(1), new("Reviewed destination")));
        if (unnamed) { target = target with { Metadata = null }; }
        var message = new ConversationMessage(request, new(1), 1, 2, DateTimeOffset.UnixEpoch, "original",
            ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
        f.ViewModel.BindSessionRouting((_, _, _) => Task.FromResult<SessionRoutingReceipt?>(
            new(target, new(message, new(request, new(2), message.ExpectedTaskState)))));
        await f.RunAsync("original");
        f.ViewModel.ResponseTitle.Should().Be(unnamed ? "Request destination: Unnamed conversation." : "Request destination: Reviewed destination.");
        f.ViewModel.ResponseBody.Should().Contain("HOST Unavailable / UnregisteredModel");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyActivatedRequestCannotOpenRoutingOrInvokeInference()
    {
        var f = await Fixture.CreateInitializedAsync();
        var calls = 0;
        f.ViewModel.BindSessionRouting((_, _, _) => { calls++; return Task.FromResult<SessionRoutingReceipt?>(null); });
        await f.RunAsync(f.ViewModel.AssistantName);
        f.ViewModel.ResponseTitle.Should().Be("No request was heard.");
        calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QualifiedRoutedAnswerUsesTheSharedBriefAndDetailsPresentation(bool details)
    {
        var f = await Fixture.CreateInitializedAsync();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var target = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), new(request.SessionId, new(1), new("Reviewed destination")));
        var body = details ? "Brief response:\nExact brief.\n\nDetailed response:\nMore exact retained answer details." : "Exact short answer.";
        var response = new ConversationResponse(new(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate), body);
        response.Validate();
        var message = new ConversationMessage(request, new(1), 1, 2, DateTimeOffset.UnixEpoch, "original",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None) { AssistantResponse = response };
        f.ViewModel.BindSessionRouting((_, _, _) => Task.FromResult<SessionRoutingReceipt?>(
            new(target, new(message, new(request, new(3), message.ExpectedTaskState)))));
        await f.RunAsync("original");
        f.ViewModel.ResponseBody.Should().Contain(details ? "Exact brief." : body);
        f.ViewModel.IsDetailsOfferPending.Should().Be(details);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainRoutingPreservesExactCreationAndSubmissionInspectionIdentity(bool fresh)
    {
        var f = await Fixture.CreateInitializedAsync();
        var creation = HostRequest.Create(RequestOrigin.LocalUi);
        var submitted = HostRequest.Create(RequestOrigin.LocalUi);
        var failure = new ConversationSubmissionUnconfirmedException(submitted, new IOException("unconfirmed"));
        f.ViewModel.BindSessionRouting((_, _, _) => throw (fresh ? new SessionRoutingUnconfirmedException(creation, failure) : failure));
        await f.RunAsync("original");
        f.ViewModel.ResponseTitle.Should().Be("Session routing stopped.");
        f.ViewModel.ResponseBody.Should().Contain(submitted.RequestId.Value.ToString("D"));
        if (fresh) { f.ViewModel.ResponseBody.Should().Contain(creation.RequestId.Value.ToString("D")); }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("name")]
    public async Task OriginalRoutingSourceIsNotRenewedAcrossAwaitedNativeReview(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        f.ViewModel.BindSessionRouting(async (_, _, admitted) =>
        {
            admitted().Should().BeTrue();
            if (change is "edit") { f.ViewModel.CommandText = "different input"; }
            else { await f.ViewModel.SetAssistantNameAsync("Nova"); }
            admitted().Should().BeFalse();
            return null;
        });
        await f.RunAsync("original");
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task PendingQuestionOwnsInputBeforeGeneralRouting()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which choice?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("please choose this work");
        await f.ViewModel.ActiveReasoningTask!;
        var calls = 0;
        f.ViewModel.BindSessionRouting((_, _, _) => { calls++; return Task.FromResult<SessionRoutingReceipt?>(null); });
        await f.RunAsync("Kora, option one");
        calls.Should().Be(0);
    }
}
