using AwesomeAssertions;
using System.Diagnostics;
using Kora.Application.Presentation;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Presentation;

[Collection("Host tracing")]
public sealed class AutomaticDetailIntentTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public AutomaticDetailIntentTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData("current")]
    [InlineData("unavailable")]
    [InlineData("mode")]
    [InlineData("expired")]
    [InlineData("native")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("session")]
    [InlineData("origin")]
    [InlineData("source-session")]
    [InlineData("request")]
    [InlineData("task")]
    public async Task ExactHostIntentIsSingleUseAndEveryUnknownOrChangedBoundaryFailsClosed(string boundary)
    {
        await using var f = new DetailPresentationConfigurationServiceTests.Fixture();
        f.Service.CaptureAutomaticPreference().Should().BeNull();
        f.Service.Observe();
        f.Service.CaptureAutomaticPreference().Should().BeNull();
        await f.Set(DetailPresentationMode.OpenAutomatically);
        var epoch = f.Service.CaptureAutomaticPreference()!;
        f.Service.IsAutomaticCurrent(epoch).Should().BeTrue();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var other = HostRequest.Create(RequestOrigin.LocalUi);
        var source = new DetailSessionSource(boundary is "source-session" ? other.SessionId : request.SessionId,
            boundary is "request" ? other.RequestId : request.RequestId,
            boundary is "task" ? other.TaskId : request.TaskId);
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            boundary is "origin" ? DetailContentOrigin.EmbeddedDocument : DetailContentOrigin.FinalizedResponse,
            DetailSensitivity.DisclosureConfirmationRequired, "Exact result", "Original finalized source", "# Full answer",
            boundary is "origin" ? null : source);
        var nativeCurrent = boundary is not "native";
        var sourceCurrent = boundary is not "source";
        var privateCurrent = boundary is not "private";
        var lease = new AutomaticDetailLease(boundary is "session" ? other.SessionId : request.SessionId, new(1),
            1, 2, 3, () => nativeCurrent, () => privateCurrent);
        var intent = new AutomaticDetailIntent(request, content, f.Service, epoch, lease, () => sourceCurrent);
        if (boundary is "unavailable") { f.Service.HoldUnavailable(); }
        if (boundary is "mode") { await f.Set(DetailPresentationMode.LinkOnly); }
        if (boundary is "expired") { f.Time.Now = epoch.Expires; }
        intent.IsCurrent().Should().Be(boundary is "current");
        intent.TryConsume().Should().Be(boundary is "current");
        intent.TryConsume().Should().BeFalse();
        if (boundary is "current")
        {
            nativeCurrent = false;
            intent.IsCurrent().Should().BeFalse();
            intent.IsSourceCurrent().Should().BeTrue("request-local input is not continuing read authority");
            privateCurrent = false;
            intent.IsSourceCurrent().Should().BeFalse();
        }
    }

    [Fact]
    public async Task ReenabledModeCannotRenewEarlierAutomaticEpochAndInspectionCannotExtendItsDeadline()
    {
        await using var f = new DetailPresentationConfigurationServiceTests.Fixture();
        await f.Set(DetailPresentationMode.OpenAutomatically);
        var epoch = f.Service.CaptureAutomaticPreference()!;
        f.Time.Now = f.Time.Now.AddSeconds(1);
        await f.Service.InspectAsync(RequestOrigin.LocalUi, static () => true, f.Token);
        f.Service.CaptureAutomaticPreference()!.Epoch.Should().Be(epoch.Epoch);
        await f.Set(DetailPresentationMode.Offer);
        f.Service.IsAutomaticCurrent(epoch).Should().BeFalse();
        await f.Set(DetailPresentationMode.OpenAutomatically);
        f.Service.IsAutomaticCurrent(epoch).Should().BeFalse();
        f.Service.CaptureAutomaticPreference()!.Epoch.Should().NotBe(epoch.Epoch);
    }
}
