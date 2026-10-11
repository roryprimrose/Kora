using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Providers;
using Kora.Core.Auditing;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("model", "compatible-model", false)]
    [InlineData("reasoning", "exact-effort", false)]
    [InlineData("context", "exact-window", false)]
    [InlineData("model", "compatible-model", true)]
    [InlineData("reasoning", "exact-effort", true)]
    [InlineData("context", "exact-window", true)]
    public async Task ShortOriginalInputFieldRouteReviewsWholeEffectiveTupleAndConfirmsOnlySparseChoice(string field, string id, bool voice)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        policy.Profiles.Catalogue.FieldCatalogue = true;
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        async Task Run(string text)
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        await Run("set providers.session." + field + " for " + policy.Session.SessionId.Value.ToString("D") + " to " + id + " for account at 1");
        policy.Snapshot.Revision.Should().Be(0);
        using var body = JsonDocument.Parse(f.ViewModel.ResponseBody);
        body.RootElement.GetProperty("PreviousEffective").GetProperty("Model").GetProperty("Value").GetString().Should().Be("exact-model");
        body.RootElement.GetProperty("ResultingEffective").GetProperty("Service").GetString().Should().Be(nameof(ProviderService.Local));
        await Run(body.RootElement.GetProperty("confirmation").GetString()!);
        policy.Snapshot.Revision.Should().Be(1);
        policy.Snapshot.Desired.Service.Should().BeNull();
        policy.Snapshot.Desired.Model.Should().Be(field is "model" ? new ProviderChoiceId(id) : null);
        policy.Snapshot.Desired.Reasoning.Should().Be(field is "reasoning" ? new ProviderChoiceId(id) : null);
        policy.Snapshot.Desired.Context.Should().Be(field is "context" ? new ProviderChoiceId(id) : null);
        policy.Profiles.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        policy.Store.Tasks.Should().OnlyContain(task => task.Request.Origin == (voice ? Kora.Core.Hosting.RequestOrigin.ActivatedVoice : Kora.Core.Hosting.RequestOrigin.LocalUi));
    }

    [Fact]
    public async Task FabricatedShortCommandWithoutFieldCannotReviewOrWritePolicy()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ExecuteSessionProviderProfileCommand(new(AppearanceCommandOperation.Set, policy.Session.SessionId.Value,
            Account: "account", CatalogueRevision: 1, Choice: "exact-model"), SecurityAuditInitiator.TypedCommand);
        policy.Snapshot.Revision.Should().Be(0);
        f.ViewModel.ResponseTitle.Should().Be("Session provider policy not confirmed.");
    }

    [Theory]
    [InlineData("unauthorized")]
    [InlineData("disposed")]
    [InlineData("privacy")]
    [InlineData("missing-session")]
    [InlineData("disposed-read")]
    [InlineData("unset")]
    [InlineData("revision")]
    [InlineData("missing-account")]
    public async Task SessionProfileControlRejectsUnadmittedOriginsAndUnresolvedDestinations(string failure)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture(current: failure is not "missing-account");
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        await f.ViewModel.InitializeAsync();
        if (failure is "disposed") { f.ViewModel.Dispose(); }
        if (failure is "privacy") { f.Session.IsUnlocked = false; }
        if (failure is "disposed-read") { policy.Profiles.Catalogue.AfterObservation = () => f.ViewModel.Dispose(); }
        if (failure is "unset") { policy.Profiles.Preferences.Value = new(ProviderService.Unset, []); }
        var command = failure is "unset" or "revision" or "missing-account"
            ? new SessionProviderProfileCommand(AppearanceCommandOperation.Set, policy.Session.SessionId.Value,
                Model: "exact-model", Reasoning: "managed-effort", Context: "managed-window",
                Account: "account", CatalogueRevision: failure is "revision" ? 2 : 1)
            : new(AppearanceCommandOperation.Get, failure is "missing-session" ? null : policy.Session.SessionId.Value);
        await f.ViewModel.ExecuteSessionProviderProfileCommand(command,
            failure is "unauthorized" ? SecurityAuditInitiator.System : SecurityAuditInitiator.LocalUser);
        policy.Snapshot.Revision.Should().Be(0);
        policy.Profiles.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExactTupleReviewAndStaleConfirmationRemainSeparateFromAppAndGeneration()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        await f.ViewModel.InitializeAsync();
        var session = policy.Session.SessionId.Value.ToString("D");
        await f.RunAsync("set providers.session.profile for " + session + " to exact-model managed-effort managed-window for account at 1");
        using var body = JsonDocument.Parse(f.ViewModel.ResponseBody);
        policy.Snapshot = policy.Snapshot with { Revision = 1 };
        await f.RunAsync(body.RootElement.GetProperty("confirmation").GetString()!);
        f.ViewModel.ResponseTitle.Should().Be("Session provider policy not confirmed.");
        policy.Snapshot.Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        await f.RunAsync("set providers.session.service for " + session + " to Claude");
        await f.RunAsync("get providers.session.profile for " + session);
        policy.Snapshot.Desired.Service.Should().BeNull();
    }

    [Fact]
    public async Task CompleteSessionDiscoveryNeverTruncatesAnOversizedFakeCatalogue()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        policy.Profiles.Catalogue.LargeCatalogue = true;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("get providers.session.profile for " + policy.Session.SessionId.Value.ToString("D"));
        f.ViewModel.ResponseTitle.Should().Be("Session provider policy not confirmed.");
        f.ViewModel.ResponseBody.Should().Contain("complete byte bound");
        policy.Snapshot.Revision.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
    [Fact]
    public async Task TypedExactSessionReviewAndConfirmationNeverWritesAppOrInvokesReasoner()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture();
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        await f.ViewModel.InitializeAsync();
        var session = policy.Session.SessionId.Value.ToString("D");
        await f.RunAsync("get providers.session.profile for " + session);
        f.ViewModel.ResponseBody.Should().Contain("Unavailable").And.Contain(session);
        await f.RunAsync("set providers.session.service for " + session + " to Claude");
        policy.Snapshot.Revision.Should().Be(0);
        using var body = JsonDocument.Parse(f.ViewModel.ResponseBody);
        await f.RunAsync(body.RootElement.GetProperty("confirmation").GetString()!);
        policy.Snapshot.Desired.Service.Should().Be(ProviderService.Claude);
        policy.Profiles.Preferences.Writes.Should().Be(0);
        await f.RunAsync("reset providers.session.profile for " + session);
        using var reset = JsonDocument.Parse(f.ViewModel.ResponseBody);
        await f.RunAsync(reset.RootElement.GetProperty("confirmation").GetString()!);
        policy.Snapshot.Desired.Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ActivatedCurrentNameUsesSameOriginalSessionAuthorityAndSeparateConfirmation()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture();
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        var session = policy.Session.SessionId.Value.ToString("D");
        await f.RaiseActivatedTranscriptAsync("Kora, set providers.session.service for " + session + " to Codex", 1);
        using var body = JsonDocument.Parse(f.ViewModel.ResponseBody);
        await f.RaiseActivatedTranscriptAsync("Kora, " + body.RootElement.GetProperty("confirmation").GetString(), 1);
        policy.Snapshot.Desired.Service.Should().Be(ProviderService.Codex);
        policy.Store.Tasks.Should().OnlyContain(task => task.Request.Origin == Kora.Core.Hosting.RequestOrigin.ActivatedVoice);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("syntax")]
    [InlineData("confirm")]
    [InlineData("account")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    public async Task MissingSessionAuthorityAndUnknownExactChoicesNeverFallbackOrGenerate(string failure)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        if (failure is not "missing") { f.ViewModel.BindSessionProviderProfiles(policy.Service); }
        await f.ViewModel.InitializeAsync();
        var session = policy.Session.SessionId.Value.ToString("D");
        var text = failure switch
        {
            "missing" => "reset providers.session.profile for " + session,
            "syntax" => "reset providers.session.profile",
            "confirm" => "confirm providers.session.profile " + Guid.NewGuid().ToString("D"),
            _ => "set providers.session.profile for " + session + " to "
                + (failure is "model" ? "other" : "exact-model") + " "
                + (failure is "reasoning" ? "other" : "managed-effort") + " "
                + (failure is "context" ? "other" : "managed-window") + " for "
                + (failure is "account" ? "other" : "account") + " at 1",
        };
        await f.RunAsync(text);
        policy.Snapshot.Revision.Should().Be(0);
        policy.Profiles.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
