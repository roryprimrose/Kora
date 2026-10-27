using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("owner")]
    [InlineData("call")]
    public async Task CapturedEvidenceAdmissionDoesNotSurviveOwnerOrCallRevisionChanges(string change)
    {
        var fixture = new Fixture();
        var admitted = fixture.ViewModel.CaptureEvidenceAnswerAdmission();
        admitted().Should().BeTrue();
        if (change is "owner") { fixture.ViewModel.BindClipboardOwnershipGate(() => false); }
        else
        {
            await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi,
                fixture.ViewModel.CallPolicyRevision, TestContext.Current.CancellationToken);
        }
        admitted().Should().BeFalse();
        fixture.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("local", true)]
    [InlineData("voice", false)]
    [InlineData("system", false)]
    [InlineData("invocation", false)]
    [InlineData("completed", false)]
    [InlineData("stopped", false)]
    public void EvidenceAnswerAdmissionRequiresCurrentOrdinaryOriginalInput(string origin, bool expected)
    {
        var fixture = new Fixture();
        var request = HostRequest.Create(origin is "voice" ? RequestOrigin.ActivatedVoice
            : origin is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (origin is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var root = origin is "none" ? null : HostActivity.BeginRoot(request,
            HostActivityLayer.Application, HostOperation.Request);
        if (origin is "completed") { root!.Complete(HostOperationOutcome.Completed); }
        if (origin is "stopped") { root!.Activity!.Stop(); }
        var admitted = fixture.ViewModel.CaptureEvidenceAnswerAdmission();
        admitted().Should().Be(expected);
        fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true);
        admitted().Should().BeFalse();
        fixture.ViewModel.Dispose();
        admitted().Should().BeFalse();
    }
}
