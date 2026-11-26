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

        f.HostStore.Records.Should().BeEmpty();
    }


}
