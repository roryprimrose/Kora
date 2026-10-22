using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class DetailWindowControllerTests
{
    [Fact]
    public async Task CurrentResponseDetailsRequireExactNativeAcknowledgementAndRetireWithoutCopy()
    {
        using var f = new Fixture();
        var content = OrdinaryContent();
        var eligible = true;
        f.Controller.OpenCurrentResponse(content, null, () => eligible).Should().BeTrue();
        f.Controller.OpenCurrentResponse(content, null, () => eligible).Should().BeTrue();
        var view = f.Views.Single();
        view.Activations.Should().Be(2);
        view.State.ActiveText.Should().Be(content.Source);
        await f.Controller.CopyAsync(content.Reference, view.State.Generation, true);
        f.Clipboard.Writes.Should().BeEmpty();
        view.State.Status.Should().Contain("no clipboard authority");
        eligible = false;
        f.Controller.RetireUnavailableCurrentResponses();
        view.Cleared.Should().BeTrue();
        view.State.ActiveText.Should().BeEmpty();
        f.Controller.OpenCurrentResponse(content, null, () => eligible).Should().BeFalse();
        f.Controller.Dispose();
        f.Controller.OpenCurrentResponse(content, null, static () => true).Should().BeFalse();
    }

    [Theory]
    [InlineData("private")]
    [InlineData("create")]
    [InlineData("activation")]
    [InlineData("show-revoked")]
    [InlineData("activate-revoked")]
    public void CurrentResponseDetailsNeverAcknowledgeFailedOrRevokedPublication(string failure)
    {
        using var f = new Fixture();
        var content = OrdinaryContent();
        var admitted = true;
        if (string.Equals(failure, "private", StringComparison.Ordinal)) { f.Accessible = false; }
        if (string.Equals(failure, "create", StringComparison.Ordinal)) { f.FailCreate = true; }
        if (failure is "activation" or "show-revoked" or "activate-revoked")
        {
            f.OnViewCreated = view =>
            {
                view.FailActivation = string.Equals(failure, "activation", StringComparison.Ordinal);
                if (string.Equals(failure, "show-revoked", StringComparison.Ordinal)) { view.OnShow = () => admitted = false; }
                if (string.Equals(failure, "activate-revoked", StringComparison.Ordinal)) { view.OnActivate = () => admitted = false; }
            };
        }
        f.Controller.OpenCurrentResponse(content, null, () => admitted).Should().BeFalse();
        f.Views.Should().OnlyContain(view => view.Cleared);
    }

    [Fact]
    public void CurrentResponseDetailsRejectForeignOriginAndChangedExactSnapshotAndPrivacyClearsAll()
    {
        using var f = new Fixture();
        var content = OrdinaryContent();
        f.Controller.OpenCurrentResponse(WebContent("web"), null, static () => true).Should().BeFalse();
        f.Controller.OpenCurrentResponse(content, null, static () => true).Should().BeTrue();
        var changed = new AdmittedDetailContent(content.Reference, content.Kind, content.Origin, content.Sensitivity,
            content.Title, content.Provenance, "Different replacement", content.SessionSource);
        f.Controller.OpenCurrentResponse(changed, null, static () => true).Should().BeFalse();
        f.Views.Should().ContainSingle();
        f.Controller.ClearForPrivacy();
        f.Views.Single().Cleared.Should().BeTrue();
    }

    private static AdmittedDetailContent OrdinaryContent()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        return new(new(new(Guid.NewGuid()), 1), DetailContentKind.PlainText, DetailContentOrigin.FinalizedResponse,
            DetailSensitivity.DisclosureConfirmationRequired, "Ordinary response", "Exact host response", "Title\r\nOriginal body",
            new(request.SessionId, request.RequestId, request.TaskId));
    }
}
