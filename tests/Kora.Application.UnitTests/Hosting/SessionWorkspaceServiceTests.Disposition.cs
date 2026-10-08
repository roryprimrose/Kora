using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task Disposition_requires_host_held_preview_and_commits_only_one_exact_original_control()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        fixture.TaskWrites.Should().BeEmpty();
        var receipt = await fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        receipt.SessionId.Should().Be(fixture.Session.SessionId);
        receipt.Generation.Value.Should().Be(2);
        receipt.Removed.Should().Be(preview);
        fixture.TaskWrites.Should().HaveCount(2);
        fixture.TaskWrites[0].Request.Should().NotBe(fixture.Request);
        fixture.TaskWrites[0].Request.Origin.Should().Be(RequestOrigin.LocalUi);
        fixture.TaskWrites[1].State.Should().Be(HostTaskState.Succeeded);
        var replay = () => fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        await replay.Should().ThrowAsync<InvalidOperationException>().WithMessage("*live native*");
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("gate")]
    [InlineData("revision")]
    [InlineData("admission")]
    public async Task Unadmitted_or_stale_confirmations_never_record_intent(string mode)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        if (string.Equals(mode, "missing", StringComparison.Ordinal))
        {
            await fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
            fixture.TaskWrites.Clear();
        }
        if (string.Equals(mode, "foreign", StringComparison.Ordinal)) { preview = preview with { ConfirmationId = Guid.NewGuid() }; }
        if (string.Equals(mode, "gate", StringComparison.Ordinal)) { fixture.CanControl = false; }
        if (string.Equals(mode, "revision", StringComparison.Ordinal)) { fixture.AdvanceRevision(); }
        var act = () => fixture.Service.ConfirmDispositionAsync(preview,
            string.Equals(mode, "origin", StringComparison.Ordinal) ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi,
            () => !string.Equals(mode, "admission", StringComparison.Ordinal), fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_gate_changes_fail_closed(bool revise)
    {
        using var fixture = new Fixture();
        fixture.AfterPreview = () =>
        {
            if (revise) { fixture.AdvanceRevision(); }
            else { fixture.CanControl = false; }
        };
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var act = () => fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unchanged private*");
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Revision_change_during_resolution_denies_even_after_confirmation_consumed()
    {
        using var fixture = new Fixture { RevokeDuringDispositionResolution = true };
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        var act = () => fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_confirmation_arguments_are_explicit_errors()
    {
        using var fixture = new Fixture();
        var act = () => fixture.Service.ConfirmDispositionAsync(null!, RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<ArgumentNullException>();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var preview = await fixture.Service.PreviewDispositionAsync(fixture.Session.SessionId, new(1), 0, fixture.Token);
        act = () => fixture.Service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, null!, fixture.Token);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
