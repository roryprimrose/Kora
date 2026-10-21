using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task JsonSyntaxStaysNativeAndDoesNotRenewCaptureOrCreateRetentionInferenceOrSpeech()
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "{\"private-marker\":1}";
        await fixture.ViewModel.PreviewClipboardAsync();
        var source = fixture.ViewModel.ClipboardPreview!;
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var transcript = fixture.ViewModel.Transcript;
        var admission = fixture.ViewModel.CaptureClipboardJsonEligibility(source);
        fixture.ViewModel.ValidateClipboardJson(source, admission, TestContext.Current.CancellationToken).Outcome
            .Should().Be(ClipboardJsonSyntaxOutcome.Valid);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.Transcript.Should().Be(transcript);
        fixture.ViewModel.ClipboardPreview.Should().BeSameAs(source);
        fixture.ViewModel.ClipboardExplanation.Should().BeNull();
        fixture.ClipboardReader.Calls.Should().Be(1);
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("owner-rebind")]
    [InlineData("owner-revision")]
    [InlineData("call-owner-rebind")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("revoke")]
    [InlineData("replace")]
    public async Task JsonAdmissionBindsOriginalInputPrivateOwnerCallAndSourceAndCannotResurrect(string change)
    {
        var fixture = new Fixture();
        var ownerRevision = 1L;
        fixture.ViewModel.BindCallOwnershipGate(() => true, () => ownerRevision);
        await fixture.ViewModel.PreviewClipboardAsync();
        var source = fixture.ViewModel.ClipboardPreview!;
        var admission = fixture.ViewModel.CaptureClipboardJsonEligibility(source);
        admission().Should().BeTrue();
        switch (change)
        {
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
            case "privacy": fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
            case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "owner-rebind": fixture.ViewModel.BindClipboardOwnershipGate(() => true); break;
            case "owner-revision": ownerRevision++; break;
            case "call-owner-rebind": fixture.ViewModel.BindCallOwnershipGate(() => true); break;
            case "call":
                await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
                    TestContext.Current.CancellationToken); break;
            case "dispose": fixture.ViewModel.Dispose(); break;
            case "revoke": await fixture.ViewModel.RevokeClipboardAsync(); break;
            default: await fixture.ViewModel.PreviewClipboardAsync(); break;
        }
        admission().Should().BeFalse();
        fixture.ViewModel.BindClipboardOwnershipGate(() => true);
        admission().Should().BeFalse();
        fixture.ViewModel.ValidateClipboardJson(source, admission, CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Unavailable);
    }

    [Fact]
    public async Task InvalidSyntaxAndCancellationAreNotGeneralResponses()
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "{private-marker";
        await fixture.ViewModel.PreviewClipboardAsync();
        var source = fixture.ViewModel.ClipboardPreview!;
        var admission = fixture.ViewModel.CaptureClipboardJsonEligibility(source);
        fixture.ViewModel.ValidateClipboardJson(source, admission, CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.InvalidSyntax);
        using var token = new CancellationTokenSource();
        token.Cancel();
        fixture.ViewModel.ValidateClipboardJson(source, admission, token.Token).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Cancelled);
        fixture.ViewModel.ResponseBody.Should().NotContain("private-marker").And.NotContain("Invalid JSON");
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task AmbientOriginCannotBeRelabelledAsNativeSyntaxAuthority(RequestOrigin origin)
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "true";
        await fixture.ViewModel.PreviewClipboardAsync();
        var source = fixture.ViewModel.ClipboardPreview!;
        var admission = fixture.ViewModel.CaptureClipboardJsonEligibility(source);
        using var host = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        fixture.ViewModel.ValidateClipboardJson(source, admission, CancellationToken.None).Outcome.Should().Be(origin == RequestOrigin.LocalUi
            ? ClipboardJsonSyntaxOutcome.Valid : ClipboardJsonSyntaxOutcome.Unavailable);
        fixture.ViewModel.CaptureClipboardJsonEligibility(source)().Should().Be(origin == RequestOrigin.LocalUi);
    }

    [Fact]
    public async Task UnexpectedInternalFailureIsNotInvalidSyntaxOrGeneralResponse()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        var source = fixture.ViewModel.ClipboardPreview!;
        var admission = fixture.ViewModel.CaptureClipboardJsonEligibility(source);
        fixture.ViewModel.BindClipboardOwnershipGate(() => throw new InvalidOperationException("private-marker"));
        fixture.ViewModel.ValidateClipboardJson(source, admission, CancellationToken.None).Outcome.Should().Be(ClipboardJsonSyntaxOutcome.Failed);
        fixture.ViewModel.ResponseBody.Should().NotContain("private-marker").And.NotContain("Invalid JSON");
    }
}
