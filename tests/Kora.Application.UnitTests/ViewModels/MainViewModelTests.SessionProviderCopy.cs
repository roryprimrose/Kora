using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Providers;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalTypedAndActivatedCopyUseTheSameSparseReviewAndDestinationOnlyConfirmation(bool voice)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        var tuple = policy.Profiles.Preferences.Value!.For(ProviderService.Local)!;
        policy.SourceSnapshot = policy.SourceSnapshot with { Desired = new(reasoning: tuple.Reasoning, binding: tuple) };
        policy.Snapshot = policy.Snapshot with { Desired = new(ProviderService.Claude) };
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        async Task Run(string text)
        {
            if (voice) { await f.RaiseActivatedTranscriptAsync("Kora, " + text, 1); }
            else { await f.RunAsync(text); }
        }
        await Run("copy providers.session.profile from " + policy.SourceSnapshot.Session.SessionId.Value.ToString("D")
            + " to " + policy.Session.SessionId.Value.ToString("D"));
        f.ViewModel.ResponseTitle.Should().Be("Review sparse provider copy; nothing saved.");
        using var review = JsonDocument.Parse(f.ViewModel.ResponseBody);
        review.RootElement.GetProperty("source").GetProperty("Session").GetProperty("SessionId").GetProperty("Value")
            .GetGuid().Should().Be(policy.SourceSnapshot.Session.SessionId.Value);
        review.RootElement.GetProperty("destination").GetProperty("Session").GetProperty("SessionId").GetProperty("Value")
            .GetGuid().Should().Be(policy.Session.SessionId.Value);
        review.RootElement.GetProperty("proposed").GetProperty("Model").ValueKind.Should().Be(JsonValueKind.Null);
        policy.Snapshot.Revision.Should().Be(0);
        await Run(review.RootElement.GetProperty("confirmation").GetString()!);
        policy.Snapshot.Desired.Serialize().Should().Be(policy.SourceSnapshot.Desired.Serialize());
        policy.SourceSnapshot.Revision.Should().Be(0);
        policy.Snapshot.Revision.Should().Be(1);
        policy.Profiles.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task EditingCopySyntaxRetiresThePreviousReviewInsteadOfLeavingAnInvisibleConfirmation()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture();
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("copy providers.session.profile from " + policy.SourceSnapshot.Session.SessionId.Value.ToString("D")
            + " to " + policy.Session.SessionId.Value.ToString("D"));
        using var review = JsonDocument.Parse(f.ViewModel.ResponseBody);
        var confirmation = review.RootElement.GetProperty("confirmation").GetString()!;
        await f.RunAsync("copy providers.session.profile from unknown to selected");
        await f.RunAsync(confirmation);
        f.ViewModel.ResponseTitle.Should().Be("Session provider policy not confirmed.");
        policy.Snapshot.Revision.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateInputOrPrivacyChangeCannotRenderACommittedCopyAsCurrentReview(bool replaceInput)
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture();
        policy.SourceSnapshot = policy.SourceSnapshot with { Desired = new(ProviderService.Claude) };
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("copy providers.session.profile from " + policy.SourceSnapshot.Session.SessionId.Value.ToString("D")
            + " to " + policy.Session.SessionId.Value.ToString("D"));
        using var review = JsonDocument.Parse(f.ViewModel.ResponseBody);
        var confirmation = review.RootElement.GetProperty("confirmation").GetString()!;
        Task? replacement = null;
        policy.AfterCommit = () =>
        {
            if (replaceInput)
            {
                replacement = f.ViewModel.ExecuteSessionProviderProfileCommand(
                    new(AppearanceCommandOperation.Clarify, Error: SessionProviderProfileCommand.Syntax),
                    SecurityAuditInitiator.TypedCommand);
            }
            else { f.Session.IsUnlocked = false; }
        };
        await f.RunAsync(confirmation);
        if (replacement is not null) { await replacement; }
        policy.Snapshot.Revision.Should().Be(1);
        policy.Snapshot.Desired.Serialize().Should().Be(policy.SourceSnapshot.Desired.Serialize());
        policy.SourceSnapshot.Revision.Should().Be(0);
        f.ViewModel.ResponseTitle.Should().NotBe("Exact session provider policy committed.");
        policy.AfterCommit = null;
        f.Session.IsUnlocked = true;
        await f.RunAsync(confirmation);
        policy.Snapshot.Revision.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PrivacyWithdrawnDuringCopyIssuanceRetiresBothLeasesBeforeDisclosure()
    {
        var f = new Fixture();
        await using var admission = f.OutputAdmission;
        await using var policy = new SessionProviderProfileConfigurationTests.Fixture();
        f.ViewModel.BindSessionProviderProfiles(policy.Service);
        await f.ViewModel.InitializeAsync();
        var profileReads = 0;
        var timeReadsAfterDestination = 0;
        var revoked = false;
        policy.AfterProfileRead = _ => profileReads++;
        policy.Profiles.Time.AfterRead = () =>
        {
            // Destination resolution reads time first; issuance reads it after its admission guard.
            if (profileReads == 2 && ++timeReadsAfterDestination == 2)
            {
                revoked = true;
                f.Session.IsUnlocked = false;
            }
        };
        await f.RunAsync("copy providers.session.profile from " + policy.SourceSnapshot.Session.SessionId.Value.ToString("D")
            + " to " + policy.Session.SessionId.Value.ToString("D"));
        revoked.Should().BeTrue();
        policy.Snapshot.Revision.Should().Be(0);
        policy.Store.Tasks.Should().BeEmpty();
        f.ViewModel.ResponseTitle.Should().NotBe("Review sparse provider copy; nothing saved.");
        policy.AfterProfileRead = null;
        policy.Profiles.Time.AfterRead = null;
        f.Session.IsUnlocked = true;
        for (var index = 0; index < 70; index++)
        {
            var proposal = await policy.Copy();
            policy.Service.Discard(proposal.Draft);
        }
        policy.Store.Tasks.Should().BeEmpty();
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
