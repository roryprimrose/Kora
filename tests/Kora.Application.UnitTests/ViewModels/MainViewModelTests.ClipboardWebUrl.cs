using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Network;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task Native_url_facts_are_not_main_responses_transcripts_models_or_audit_bodies()
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "https://EXAMPLE.com/private?token=secret#hidden";
        await fixture.ViewModel.PreviewClipboardAsync();
        var source = fixture.ViewModel.ClipboardPreview!;
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var transcript = fixture.ViewModel.Transcript;
        var result = fixture.ViewModel.InspectClipboardWebUrl(source, () => true, CancellationToken.None);
        result.Outcome.Should().Be(WebUrlSyntaxOutcome.Accepted);
        result.Facts!.CanonicalAsciiHost.Should().Be("example.com");
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.Transcript.Should().Be(transcript);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Audit.Events.Should().BeEmpty();
        fixture.ViewModel.ClipboardPreview.Should().BeSameAs(source);
        fixture.ViewModel.InspectClipboardWebUrl(source, () => false, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Stale);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        fixture.ViewModel.InspectClipboardWebUrl(source, () => true, cancel.Token).Outcome.Should().Be(WebUrlSyntaxOutcome.Cancelled);
        fixture.ViewModel.ClearClipboardPreview();
        fixture.ViewModel.InspectClipboardWebUrl(source, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Stale);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("private")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("exit")]
    [InlineData("handoff")]
    public async Task Url_inspection_cannot_survive_original_host_lifecycle(string change)
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "http://127.0.0.1/private";
        await fixture.ViewModel.PreviewClipboardAsync();
        var source = fixture.ViewModel.ClipboardPreview!;
        switch (change)
        {
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
            case "private": fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
            case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "call":
                await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                    fixture.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken); break;
            case "dispose": fixture.ViewModel.Dispose(); break;
            case "exit": await fixture.ViewModel.ExitAsync(); break;
            default: await fixture.ViewModel.TryPrepareHandoffAsync(); break;
        }
        fixture.ViewModel.InspectClipboardWebUrl(source, () => true, CancellationToken.None).Outcome.Should().Be(WebUrlSyntaxOutcome.Stale);
    }
}
