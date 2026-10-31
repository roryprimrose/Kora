using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ObservedResultCommandCarriesExactNameAndOriginalTypedOrCurrentNameVoiceWithoutModelUse()
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RunAsync("set assistant name to Nova");
        var observed = new List<WorkspaceSurfaceNavigation>();
        f.ViewModel.WorkspaceSurfaceRequested += request =>
        {
            request.IsAdmitted().Should().BeTrue();
            observed.Add(request);
            return Task.CompletedTask;
        };
        const string name = "application.get_version message 1 result 1";
        await f.RunAsync("open observed result " + name);
        await f.RaiseActivatedTranscriptAsync("Nova open observed result " + name, 1);
        observed.Should().HaveCount(2).And.OnlyContain(request => request.Target == WorkspaceSurfaceTarget.ObservedResult
            && string.Equals(request.ObservationName, name, StringComparison.Ordinal));
        observed.Select(request => request.Origin).Should().Equal(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice);
        observed[0].IsAdmitted().Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.HostStore.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task ObservedResultCommandCannotBorrowQuestionApprovalOrEditorAuthority()
    {
        var f = new Fixture();
        f.Probe.Status = new("local.inference", "Local", Kora.Core.Dependencies.DependencyReadiness.Ready, "Ready");
        f.Reasoner.Question = new("Which target?", ["One", "Two"]);
        await f.ViewModel.InitializeAsync();
        var calls = 0;
        f.ViewModel.WorkspaceSurfaceRequested += request =>
        {
            calls++;
            return Task.CompletedTask;
        };
        await f.RunAsync("choose");
        await f.ViewModel.ActiveReasoningTask!;
        await f.RaiseActivatedTranscriptAsync("Kora open observed result application.get_version", 1);
        f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        calls.Should().Be(0);
        await f.ViewModel.CancelModelQuestionAsync();
        f.ViewModel.PublishCustomizationVoiceTarget(EditorVoiceTarget());
        await f.RaiseActivatedTranscriptAsync("Kora open observed result application.get_version", 1);
        calls.Should().Be(0);
        f.ViewModel.PublishCustomizationVoiceTarget(null);
        await f.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        await f.RunAsync("open observed result application.get_version");
        f.ViewModel.IsGrantChangePending.Should().BeTrue();
        calls.Should().Be(0);
        await f.ViewModel.RejectPendingGrantChangeAsync();
    }
}
