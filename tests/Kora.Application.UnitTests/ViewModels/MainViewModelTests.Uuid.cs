using AwesomeAssertions;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Utilities;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    private sealed class UuidGenerator : IUtilityUuidGenerator
    {
        internal const string Value = "12345678-9abc-4def-8123-456789abcdef";
        internal int Calls { get; private set; }
        internal Exception? Failure { get; set; }
        internal Guid Output { get; set; } = Guid.Parse(Value);
        internal Action? Callback { get; set; }
        public Guid Generate()
        {
            Calls++;
            Callback?.Invoke();
            if (Failure is not null) { throw Failure; }
            return Output;
        }
    }

    [Fact]
    public async Task UuidCommandsShareActionsWithoutIdentityRegistrationOrModelFallback()
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        using var hostile = new System.Diagnostics.Activity("untrusted").AddBaggage("kora.session.id", UuidGenerator.Value).Start();
        await f.RunAsync("uuid generate");
        f.ViewModel.ResponseBody.Should().Contain(UuidGenerator.Value).And.Contain("not Kora identity");
        generator.Calls.Should().Be(1);
        await f.RunAsync("uuid inspect " + UuidGenerator.Value);
        f.ViewModel.ResponseBody.Should().Contain("version nibble 4; RFC variant");
        await f.RunAsync("uuid help");
        f.ViewModel.ResponseBody.Should().Contain(Kora.Tools.Utilities.UuidHelp.Syntax);
        generator.Calls.Should().Be(1);
        System.Diagnostics.Activity.Current.Should().BeSameAs(hostile);
        f.HostStore.Records.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
        f.Audit.Events.Should().NotContain(item => item.TargetId == UuidGenerator.Value);
        f.Session.LockCalls.Should().Be(0);
        await f.RunAsync("set assistant name to Nova");
        await f.RaiseActivatedTranscriptAsync("Nova, uuid generate", 1);
        generator.Calls.Should().Be(2);
        f.Voice.StartedPhrases.Should().Contain("uuid generate");
        await f.RaiseActivatedTranscriptAsync("uuid generate", 1);
        generator.Calls.Should().Be(2);
        f.ViewModel.Transcript.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData("question")]
    [InlineData("approval")]
    [InlineData("grant")]
    public async Task UuidReservedCommandsPreservePendingDecisions(string pending)
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        await f.ViewModel.InitializeAsync();
        switch (pending)
        {
            case "question": f.Reasoner.Question = new("Which?", ["One", "Two"]); break;
            case "approval": f.Reasoner.Action = BuiltInAction.LockMachine; break;
            case "grant":
                await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
                break;
        }
        if (!string.Equals(pending, "grant", StringComparison.Ordinal))
        {
            await f.RunAsync("choose");
            await f.ViewModel.ActiveReasoningTask!;
        }
        var question = f.ViewModel.IsModelQuestionPending;
        var approval = f.ViewModel.IsModelActionApprovalPending;
        var grant = f.ViewModel.IsGrantChangePending;
        var body = f.ViewModel.ResponseBody;
        f.Reasoner.Requests.Clear();
        foreach (var text in new[] { "uuid generate", "uuid inspect " + UuidGenerator.Value, "uuid help", "uuid", "uuid generate 2", "uuid inspect nope" })
        {
            await f.RunAsync(text);
            f.ViewModel.IsModelQuestionPending.Should().Be(question);
            f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
            f.ViewModel.IsGrantChangePending.Should().Be(grant);
            f.ViewModel.ResponseBody.Should().Be(body);
        }
        generator.Calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Session.LockCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("locked")]
    [InlineData("call")]
    [InlineData("name")]
    public async Task UuidUtilityFailsClosedForUnadmittedHost(string denial)
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        switch (denial)
        {
            case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "locked": f.Session.IsUnlocked = false; break;
            case "call": f.CallState.SetState(CallState.Active); await f.Dispatcher.LastInvocation; break;
            case "name": f.NamePreferences.Name = ""; await f.ViewModel.InitializeAsync(); break;
        }
        await f.RunAsync("Kora, uuid generate");
        generator.Calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("invalid")]
    [InlineData("cancel")]
    public async Task UuidProviderFailureIsExplicitAndNeverRetried(string failure)
    {
        var generator = new UuidGenerator();
        if (string.Equals(failure, "invalid", StringComparison.Ordinal)) { generator.Output = Guid.Empty; }
        else { generator.Failure = string.Equals(failure, "cancel", StringComparison.Ordinal) ? new OperationCanceledException() : new InvalidOperationException("failed"); }
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("uuid generate");
        generator.Calls.Should().Be(1);
        f.ViewModel.ResponseTitle.Should().Be("UUID utility failed.");
        f.ViewModel.ResponseBody.Should().NotContain(UuidGenerator.Value);
    }

    [Fact]
    public async Task UuidOperationHasSeparateHostCorrelationAndNoUtilityBaggage()
    {
        var generator = new UuidGenerator();
        Kora.Core.Diagnostics.HostActivity? observed = null;
        generator.Callback = () =>
        {
            observed = Kora.Core.Diagnostics.HostActivity.RequireCurrent();
            observed.Request.RequestId.Value.ToString("D").Should().NotBe(UuidGenerator.Value);
            observed.Activity!.Baggage.Should().BeEmpty();
            observed.Activity.TagObjects.Should().NotContain(tag => string.Equals(tag.Value as string, UuidGenerator.Value, StringComparison.Ordinal));
        };
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        using var hostile = new System.Diagnostics.Activity("incoming").AddBaggage("kora.session.id", UuidGenerator.Value).Start();
        await f.RunAsync("uuid generate");
        observed.Should().NotBeNull();
        observed!.Outcome.Should().Be(Kora.Core.Diagnostics.HostOperationOutcome.Completed);
        System.Diagnostics.Activity.Current.Should().BeSameAs(hostile);
    }

    [Theory]
    [InlineData("call")]
    [InlineData("owner")]
    [InlineData("privacy")]
    public async Task UuidProviderCannotPublishAfterAdmissionRevocation(string loss)
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        generator.Callback = () =>
        {
            switch (loss)
            {
                case "call": f.CallState.SetState(CallState.Active); break;
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
                case "privacy": f.Session.IsUnlocked = false; break;
            }
        };
        await f.RunAsync("uuid generate");
        generator.Calls.Should().Be(1);
        f.ViewModel.ResponseBody.Should().NotContain(UuidGenerator.Value);
        f.HostStore.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Kora.Core.Auditing.SecurityAuditInitiator.ModelSuggestion)]
    [InlineData(Kora.Core.Auditing.SecurityAuditInitiator.LocalUser)]
    [InlineData((Kora.Core.Auditing.SecurityAuditInitiator)999)]
    public async Task UuidPrivateBoundaryRejectsNonCommandInitiators(Kora.Core.Auditing.SecurityAuditInitiator initiator)
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        var routing = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
            "HandleTranscriptAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)routing.Invoke(f.ViewModel, ["uuid generate", 1f, initiator, null, null, null])!;
        generator.Calls.Should().Be(0);
    }

    [Fact]
    public async Task UuidUnknownInternalOperationFailsRatherThanReturningHelp()
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        var route = typeof(Kora.Application.ViewModels.MainViewModel).GetMethod(
            "ExecuteUuidCommandAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)route.Invoke(f.ViewModel, [new Kora.Application.Tools.UuidCommand((Kora.Application.Tools.UuidOperation)999),
            Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, null])!;
        generator.Calls.Should().Be(0);
        f.ViewModel.ResponseTitle.Should().Be("UUID utility failed.");
    }

    [Fact]
    public async Task UuidUtilityRejectsExternalOriginAndNameCollisionVoiceWithoutPrefix()
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        await Kora.Application.Hosting.HostRequestRunner.RunAsync(Kora.Core.Hosting.RequestOrigin.HostSystem,
            () => f.RunAsync("uuid generate"));
        generator.Calls.Should().Be(0);
        await f.RunAsync("set assistant name to uuid");
        await f.RaiseActivatedTranscriptAsync("uuid generate", 1);
        generator.Calls.Should().Be(0);
        await f.RaiseActivatedTranscriptAsync("uuid, uuid generate", 1);
        generator.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UuidDoesNotReplaceNativeSecurityPreview(bool diagnostic)
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator,
            enableAuditRetention: !diagnostic, enableAuditImpact: !diagnostic,
            enableDiagnosticRetention: diagnostic, enableDiagnosticImpact: diagnostic);
        await f.ViewModel.InitializeAsync();
        if (diagnostic)
        {
            f.ViewModel.BindDiagnosticRetentionNativeLifetime(static () => true);
            f.ViewModel.ProposedDiagnosticImpactDays = 1;
            await f.ViewModel.PreviewDiagnosticImpactCommand.ExecuteAsync();
        }
        else
        {
            f.ViewModel.BindAuditRetentionNativeLifetime(static () => true);
            f.ViewModel.ProposedAuditImpactDays = 30;
            await f.ViewModel.PreviewAuditImpactCommand.ExecuteAsync();
        }
        var status = diagnostic ? f.ViewModel.DiagnosticImpactPreviewStatus : f.ViewModel.AuditImpactPreviewStatus;
        await f.RunAsync("uuid generate");
        generator.Calls.Should().Be(0);
        (diagnostic ? f.ViewModel.DiagnosticImpactPreviewStatus : f.ViewModel.AuditImpactPreviewStatus).Should().Be(status);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task UuidLeavesProviderReviewAndOriginalConfirmationUnchanged()
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator, enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = Kora.Core.Providers.ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(Kora.Core.Providers.ProviderService.Local,
            [Kora.Application.UnitTests.Configuration.ProviderProfileFieldConfigurationTests.BaseTuple()]);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("set providers.app.model to compatible-model for account at 1");
        var confirmation = f.ViewModel.ProviderFieldConfirmationId;
        confirmation.Should().NotBeNull();
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("uuid generate");
        await f.RunAsync("uuid inspect nope");
        generator.Calls.Should().Be(0);
        f.ViewModel.ProviderFieldConfirmationId.Should().Be(confirmation);
        f.ViewModel.ResponseBody.Should().Be(body);
        f.ProfilePreferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task UuidKeepsOrdinaryVisualOnlySpeechPolicyAndRejectsMalformedVoice()
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        f.ViewModel.DefaultResponseMode = Kora.Core.Voice.ResponseOutputMode.VisualOnly;
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync("uuid generate");
        f.TextToSpeech.SpokenText.Should().BeNull();
        await f.RaiseActivatedTranscriptAsync("Kora, uuid generate 2", 1);
        await f.RaiseActivatedTranscriptAsync("Kora, uuid inspect \uFF11", 1);
        generator.Calls.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("privacy")]
    [InlineData("owner")]
    public async Task UuidRejectsRetiredVoiceInputWithoutGenerating(string loss)
    {
        var generator = new UuidGenerator();
        var f = new Fixture(uuidGenerator: generator);
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.SetVoiceConsentAsync(true);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        f.Dispatcher.BeforeInvoke = () =>
        {
            switch (loss)
            {
                case "generation": f.Voice.AdvanceGeneration(); break;
                case "privacy": f.Session.IsUnlocked = false; break;
                case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            }
        };
        await f.Voice.RaiseTranscriptAsync("Kora, uuid generate", 1);
        generator.Calls.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
