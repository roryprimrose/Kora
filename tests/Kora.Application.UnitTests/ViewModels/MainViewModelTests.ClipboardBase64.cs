using AwesomeAssertions;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NativeBase64InspectionNeverChangesOriginalSourceResponseTranscriptOrModelState()
    {
        var logger = new ResponseReadLogger();
        var fixture = new Fixture(logger: logger);
        fixture.ClipboardReader.Text = "c3ludGhldGljIHNlY3JldA==";
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        var title = fixture.ViewModel.ResponseTitle;
        var response = fixture.ViewModel.ResponseBody;
        var transcript = fixture.ViewModel.Transcript;
        fixture.ViewModel.InspectClipboardBase64(snapshot, TestContext.Current.CancellationToken)
            .Should().Be(new ClipboardBase64Inspection.AdmittedProfile(16));
        fixture.ViewModel.ClipboardPreview.Should().BeSameAs(snapshot);
        fixture.ClipboardReader.Calls.Should().Be(1);
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(response);
        fixture.ViewModel.Transcript.Should().Be(transcript);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Audit.Events.Should().BeEmpty();
        logger.Messages.Should().NotContain(message => message.Contains(snapshot.Text, StringComparison.Ordinal)
            || message.Contains("synthetic secret", StringComparison.Ordinal));
        fixture.ViewModel.ClearClipboardPreview();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("lock")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("dispose")]
    [InlineData("failure")]
    public async Task Base64RetainsOriginalHostPrivacyOwnershipAndCallBinding(string change)
    {
        var logger = new ResponseReadLogger();
        var fixture = new Fixture(logger: logger);
        fixture.ClipboardReader.Text = "AA==";
        await fixture.ViewModel.PreviewClipboardAsync();
        var snapshot = fixture.ViewModel.ClipboardPreview!;
        switch (change)
        {
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
            case "lock": fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
            case "owner": fixture.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "call":
                await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
                    TestContext.Current.CancellationToken); break;
            case "dispose": fixture.ViewModel.Dispose(); break;
            case "failure": fixture.ViewModel.BindClipboardOwnershipGate(() => throw new InvalidOperationException("synthetic secret must not be logged")); break;
        }
        var inspect = () => fixture.ViewModel.InspectClipboardBase64(snapshot, CancellationToken.None);
        if (change is "failure") { inspect.Should().Throw<InvalidOperationException>(); }
        else { inspect.Should().Throw<OperationCanceledException>(); }
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ClipboardReader.Calls.Should().Be(1);
        if (change is "failure")
        {
            logger.Messages.Should().Contain(message => message.Contains("Native clipboard Base64 inspection failed", StringComparison.Ordinal)
                && message.Contains("InvalidOperationException", StringComparison.Ordinal));
            logger.Messages.Should().NotContain(message => message.Contains("synthetic secret", StringComparison.Ordinal)
                || message.Contains("AA==", StringComparison.Ordinal));
            logger.Exceptions.Should().OnlyContain(exception => exception == null);
        }
    }

    [Fact]
    public async Task AmbientOriginCannotBeRelabelledByNativeInspectionEntryPoint()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.PreviewClipboardAsync();
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Request);
        var inspect = () => fixture.ViewModel.InspectClipboardBase64(fixture.ViewModel.ClipboardPreview!, CancellationToken.None);
        inspect.Should().Throw<OperationCanceledException>();
    }
}
