using System.Reflection;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private const string CatalogueSessionId = "12345678-abcd-1234-abcd-123456789abc";
    private const string PrepareCatalogue = "prepare attachment catalogue " + CatalogueSessionId;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CataloguePreparationOnlyHandsOffFutureQueryWithoutResolvingOrChangingAnything(bool voice)
    {
        var f = new Fixture();
        var presenter = new CataloguePreparationFake();
        f.ViewModel.BindAttachmentCataloguePreparation(presenter);
        if (voice)
        {
            await f.RaiseActivatedTranscriptAsync("Kora, attachment catalogue help", 1);
        }
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var audit = f.Audit.Events.ToArray();
        if (voice)
        {
            await f.ViewModel.BeginPushToTalkAsync();
            await f.ViewModel.EndPushToTalkAsync();
            title = f.ViewModel.ResponseTitle;
            body = f.ViewModel.ResponseBody;
            await f.Voice.RaiseTranscriptAsync("Kora, " + PrepareCatalogue, 1);
        }
        else { await f.RunAsync(PrepareCatalogue); }
        presenter.Ids.Should().Equal(Guid.Parse(CatalogueSessionId));
        presenter.Origins.Should().Equal(voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        f.ViewModel.Transcript.Should().Contain("PREPARED").And.Contain("No I/O").And.Contain("AND-composed")
            .And.NotContain(CatalogueSessionId);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.HostStore.Records.Should().BeEmpty();
        f.Audit.Events.Should().Equal(audit);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ClipboardReader.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("attachment catalogue help", true)]
    [InlineData("prepare attachment catalogue current", false)]
    [InlineData("attachment catalogue search \"private\"", false)]
    [InlineData("prepare\0attachment catalogue " + CatalogueSessionId, false)]
    [InlineData("pre\u200bpare attachment catalogue " + CatalogueSessionId, false)]
    public async Task CatalogueHelpAndMalformedFamilyNeverInvokeNativeOrModelRoutes(string text, bool help)
    {
        var f = new Fixture();
        var presenter = new CataloguePreparationFake();
        f.ViewModel.BindAttachmentCataloguePreparation(presenter);
        await f.RunAsync(text);
        presenter.Ids.Should().BeEmpty();
        presenter.Captures.Should().Be(0);
        if (help) { f.ViewModel.Transcript.Should().Be(AttachmentCataloguePreparationCommand.Syntax); }
        else { f.ViewModel.Transcript.Should().Contain("refused"); }
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CataloguePreparationDoesNotAnswerOrReplacePendingQuestionOrApproval(bool approval)
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", DependencyReadiness.Ready, "fixture");
        if (approval) { f.Reasoner.Action = BuiltInAction.EnableHostedModels; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("help with this machine");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var count = f.Reasoner.Requests.Count;
        f.ViewModel.BindAttachmentCataloguePreparation(new CataloguePreparationFake());
        await f.RunAsync(PrepareCatalogue);
        await f.RunAsync("attachment catalogue help");
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.Reasoner.Requests.Should().HaveCount(count);
        f.ViewModel.HostedModelsEnabled.Should().Be(false);
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("unbound")]
    [InlineData("lifetime")]
    [InlineData("locked")]
    [InlineData("private")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("owner-revision")]
    [InlineData("owner-rebound")]
    [InlineData("rebind")]
    [InlineData("disposed")]
    [InlineData("hide-reshow")]
    [InlineData("composer")]
    [InlineData("input")]
    [InlineData("name")]
    [InlineData("call")]
    public async Task CataloguePreparationRejectsOriginalAdmissionLossAcrossDispatch(string loss)
    {
        var f = new Fixture();
        var presenter = new CataloguePreparationFake();
        f.ViewModel.BindAttachmentCataloguePreparation(loss is "unbound" ? null : presenter);
        long owner = 1;
        Task? newerInput = null;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        f.Dispatcher.BeforeInvoke = () =>
        {
            switch (loss)
            {
                case "lifetime": presenter.Live = false; break;
                case "locked": f.Session.IsUnlocked = false; break;
                case "private": f.ViewModel.CloseForObservedPrivacyEvent("fixture", true); break;
                case "privacy": f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                    { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 }; break;
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "owner-revision": owner++; break;
                case "owner-rebound": f.ViewModel.BindCallOwnershipGate(static () => true); break;
                case "rebind": f.ViewModel.BindAttachmentCataloguePreparation(presenter); break;
                case "disposed": f.ViewModel.Dispose(); break;
                case "hide-reshow": presenter.Revision += 2; break;
                case "composer": f.ViewModel.CommandText = "new unsent input"; break;
                case "name": newerInput = f.ViewModel.SetAssistantNameAsync("Nova"); break;
                case "call": newerInput = f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                    f.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken); break;
                case "input":
                    newerInput = InvokeTranscriptForTest(f.ViewModel, "attachment catalogue help",
                        SecurityAuditInitiator.TypedCommand);
                    break;
            }
        };
        await f.RunAsync(PrepareCatalogue);
        if (newerInput is not null) { await newerInput; }
        presenter.Ids.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("unavailable");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("native")]
    [InlineData("dispatch")]
    [InlineData("capture")]
    [InlineData("inside")]
    public async Task CataloguePreparationRequiresNativeAcknowledgement(string failure)
    {
        var f = new Fixture();
        var presenter = new CataloguePreparationFake
        {
            Failure = failure is "native",
            CaptureFailure = failure is "capture",
            AfterPrepare = failure is "inside" ? () => f.Session.IsUnlocked = false : null,
        };
        f.ViewModel.BindAttachmentCataloguePreparation(presenter);
        if (failure is "dispatch") { f.Dispatcher.InvokeException = new InvalidOperationException("dispatch unavailable"); }
        await f.RunAsync(PrepareCatalogue);
        f.ViewModel.Transcript.Should().Contain("not acknowledged").And.NotContain("PREPARED");
    }

    [Theory]
    [InlineData("unprefixed")]
    [InlineData("voice-owner")]
    [InlineData("generation-first")]
    [InlineData("generation-second")]
    [InlineData("consent")]
    [InlineData("voice-stop")]
    [InlineData("voice-rebound-first")]
    [InlineData("voice-rebound-second")]
    [InlineData("presenter-first")]
    [InlineData("lifetime-first")]
    [InlineData("composer-first")]
    [InlineData("name-first")]
    [InlineData("name-second")]
    [InlineData("capture")]
    [InlineData("owner-rebound-first")]
    [InlineData("input-first")]
    public async Task CatalogueVoicePreparationRequiresOriginalCurrentNameVoiceEpochAndConsent(string loss)
    {
        var f = new Fixture();
        var presenter = new CataloguePreparationFake();
        f.ViewModel.BindAttachmentCataloguePreparation(presenter);
        await f.RaiseActivatedTranscriptAsync("Kora, what can you do", 1);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        Task? consentChange = null;
        if (loss is "voice-owner") { f.ViewModel.BindVoiceOwnershipGate(() => false); }
        if (loss is "generation-first") { f.Dispatcher.BeforeInvoke = f.Voice.AdvanceGeneration; }
        if (loss is "generation-second")
        { f.Dispatcher.BeforeInvoke = () => f.Dispatcher.BeforeInvoke = f.Voice.AdvanceGeneration; }
        if (loss is "consent")
        { f.Dispatcher.BeforeInvoke = () => f.Dispatcher.BeforeInvoke = () => consentChange = f.ViewModel.SetVoiceConsentAsync(false); }
        if (loss is "voice-stop")
        { f.Dispatcher.BeforeInvoke = () => f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindCallOwnershipGate(() => false); }
        if (loss is "voice-rebound-first")
        { f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindVoiceOwnershipGate(() => true); }
        if (loss is "voice-rebound-second")
        { f.Dispatcher.BeforeInvoke = () => f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindVoiceOwnershipGate(() => true); }
        if (loss is "presenter-first")
        { f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindAttachmentCataloguePreparation(presenter); }
        if (loss is "lifetime-first")
        { f.Dispatcher.BeforeInvoke = () => presenter.Revision++; }
        if (loss is "composer-first")
        { f.Dispatcher.BeforeInvoke = () => f.ViewModel.CommandText = "unsent draft"; }
        if (loss is "name-first")
        { f.Dispatcher.BeforeInvoke = () => consentChange = f.ViewModel.SetAssistantNameAsync("Nova"); }
        if (loss is "name-second")
        { f.Dispatcher.BeforeInvoke = () => f.Dispatcher.BeforeInvoke = () => consentChange = f.ViewModel.SetAssistantNameAsync("Nova"); }
        if (loss is "capture") { presenter.CaptureFailure = true; }
        if (loss is "owner-rebound-first")
        { f.Dispatcher.BeforeInvoke = () => f.ViewModel.BindCallOwnershipGate(() => true); }
        if (loss is "input-first")
        {
            f.Dispatcher.BeforeInvoke = () =>
            {
                consentChange = InvokeTranscriptForTest(f.ViewModel, "attachment catalogue help",
                    SecurityAuditInitiator.TypedCommand);
            };
        }
        await f.Voice.RaiseTranscriptAsync((loss is "unprefixed" ? "" : "Kora, ") + PrepareCatalogue, 1);
        if (consentChange is not null) { await consentChange; }
        presenter.Ids.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Fact]
    public async Task CataloguePreparationNeverAcceptsHostSystemOrigin()
    {
        var f = new Fixture();
        var presenter = new CataloguePreparationFake();
        f.ViewModel.BindAttachmentCataloguePreparation(presenter);
        await HostRequestRunner.RunAsync(RequestOrigin.HostSystem, () => f.RunAsync(PrepareCatalogue));
        presenter.Ids.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("protected")]
    [InlineData("name")]
    public async Task CataloguePreparationRefusesInitiallyUnavailableAuthority(string state)
    {
        var f = new Fixture();
        var presenter = new CataloguePreparationFake();
        f.ViewModel.BindAttachmentCataloguePreparation(presenter);
        if (state is "owner") { f.ViewModel.BindCallOwnershipGate(() => false); }
        if (state is "protected")
        {
            await f.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                f.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken);
        }
        if (state is "name")
        {
            f.NamePreferences.Name = "\0";
            await f.ViewModel.InitializeAsync();
        }
        await f.RunAsync(PrepareCatalogue);
        presenter.Ids.Should().BeEmpty();
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    public async Task CataloguePreparationRejectsNonCommandInitiators(SecurityAuditInitiator initiator)
    {
        var f = new Fixture();
        await InvokeTranscriptForTest(f.ViewModel, PrepareCatalogue, initiator);
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    private sealed class CataloguePreparationFake : IAttachmentCataloguePreparation
    {
        public bool Live { get; set; } = true;
        public long Revision { get; set; }
        public bool Failure { get; init; }
        public bool CaptureFailure { get; set; }
        public Action? AfterPrepare { get; init; }
        public int Captures { get; private set; }
        public List<Guid> Ids { get; } = [];
        public List<RequestOrigin> Origins { get; } = [];
        public Func<bool> CaptureLifetime()
        {
            Captures++;
            if (CaptureFailure) { throw new InvalidOperationException("native lifetime unavailable"); }
            var revision = Revision;
            return () => Live && Revision == revision;
        }
        public void Prepare(Guid sessionId, Func<bool> admitted)
        {
            admitted().Should().BeTrue();
            if (Failure) { throw new InvalidOperationException("native focus unavailable"); }
            Ids.Add(sessionId);
            Origins.Add(HostActivity.RequireCurrent().Request.Origin);
            AfterPrepare?.Invoke();
        }
    }
}
