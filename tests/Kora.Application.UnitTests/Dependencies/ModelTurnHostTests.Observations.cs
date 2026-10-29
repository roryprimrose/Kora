using System.Text;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("ordinary")]
    [InlineData("not-original")]
    [InlineData("no-target")]
    [InlineData("tool-free")]
    [InlineData("hosted")]
    [InlineData("invocation")]
    public async Task CaptureEligibilityRequiresEveryOriginalLocalReadOnlyFence(string scope)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var admitted = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        var provenance = admitted.Provenance;
        if (scope is "hosted") { provenance = provenance with { Selection = ModelProviderSelection.CopilotCandidate }; }
        if (scope is "invocation")
        {
            var request = provenance.Request;
            provenance = provenance with
            { Request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())) };
        }
        var turn = new Kora.Application.Dependencies.ModelTurn(admitted.Owner, admitted.Issuer, provenance,
            scope is "tool-free" ? f.Context(toolMode: ModelToolMode.None) : admitted.Context,
            admitted.Wire, admitted.SessionGeneration, admitted.TaskRevision, admitted.ControlRevision, admitted.Registration)
        {
            RetainLocalObservations = scope is not "not-original",
            ConversationTarget = scope is "no-target" ? null : new(f.Session, null),
        };
        turn.RetainsLocalObservations.Should().Be(scope is "ordinary");
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    [InlineData("readback")]
    [InlineData("credential")]
    [InlineData("cell-bound")]
    [InlineData("tool-credential")]
    [InlineData("tool-failed")]
    [InlineData("tool-malformed")]
    public async Task OriginalLocalHostOwnsObservationCaptureAndRetainsOnlyConfirmedAtomicSuccessfulAnswer(string fault)
    {
        using var f = new Fixture();
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfilePreferences.Value = new(ProviderService.Local, [Profile()]);
        var host = f.ConfiguredProfiles();
        var service = f.ConversationService(host, execution: host);
        f.ObservedVersion = fault switch
        {
            "cell-bound" => new string('b', 128),
            "tool-credential" => "api_key=synthetic-only-secret",
            "tool-failed" => new string('b', 129),
            "tool-malformed" => "\ud800",
            _ => "1.0",
        };
        string? bytes = null;
        f.Adapter.OnTool = async invoke =>
        {
            var count = fault is "cell-bound" ? 6 : 1;
            for (var index = 0; index < count; index++)
            {
                var result = await invoke(ReadOnlyCapabilityCatalog.Version, "{}", Token);
                result.Outcome.Should().Be(fault.StartsWith("tool-", StringComparison.Ordinal)
                    ? CapabilityOutcome.Denied : CapabilityOutcome.Succeeded);
                bytes = Encoding.UTF8.GetString(CapabilityReplyWire.Serialize(result));
            }
            f.Adapter.Reply = fault switch
            {
                "cancelled" => new(ModelTurnOutcome.Cancelled, ModelProviderSelection.OllamaCandidate),
                "failed" => new(ModelTurnOutcome.Unknown, ModelProviderSelection.OllamaCandidate),
                "credential" => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("password=synthetic-only-secret", null)),
                _ => new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
                    new(fault is "cell-bound" ? new string('a', 3900) : "actual observation answer", null)),
            };
        };
        if (fault is "readback") { f.TerminalReceiptFault = "observations"; }
        var submit = () => service.SubmitAsync(new(f.Session, null),
            fault is "cell-bound" ? new string('\u0800', 4096) : "original", static () => true, Token);
        if (fault is "readback")
        {
            await submit.Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
            return;
        }
        var receipt = await submit();
        if (fault is "success")
        {
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            receipt.Message.Observations!.Items.Should().ContainSingle();
            receipt.Message.Observations.Items.Should().OnlyContain(item => string.Equals(item.ResultJson, bytes, StringComparison.Ordinal));
            using var read = HostActivity.BeginRoot(
                new(new(Guid.NewGuid()), f.Session.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
                HostActivityLayer.Application, HostOperation.Request);
            var item = receipt.Message.Observations.Items[0];
            var detail = await service.ReadDetailAsync(new(f.Session, null), receipt.Message, static () => true, Token, observationId: item.Id);
            detail.Origin.Should().Be(DetailContentOrigin.RetainedLocalObservation);
            detail.Source.Should().Be(item.ResultJson);
        }
        else
        {
            receipt.Message.Outcome.Should().NotBe(ModelTurnOutcome.Succeeded);
            receipt.Message.AssistantResponse.Should().BeNull();
            receipt.Message.Observations.Should().BeNull();
        }
        f.Messages.Should().NotContain(text => text.Contains("actual observation answer", StringComparison.Ordinal)
            || text.Contains("synthetic-only-secret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("count")]
    [InlineData("credential")]
    [InlineData("shape")]
    public async Task HostCaptureRejectsRevokedBoundedAndRestrictedResultsBeforeTheyCanBecomeRetainedSources(string fault)
    {
        using var f = new Fixture();
        using var root = f.Root();
        var turn = (await f.Host.AdmitAsync(ModelProviderSelection.OllamaCandidate, f.Context(), Token)).Turn!;
        turn.ConversationTarget = new(f.Session, null);
        turn.RetainLocalObservations = true;
        turn.IsRunning = fault is not "closed";
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "observed",
            Version: new("1.0", fault is "credential" ? "api_key=synthetic-only-secret" : "deployment"));
        if (fault is "shape") { reply = reply with { Version = null }; }
        var capture = () =>
        {
            for (var index = 0; index < (fault is "count" ? 7 : 1); index++)
            { turn.CaptureObservation(ReadOnlyCapabilityCatalog.Version, "{}", reply, f.Time.Now); }
        };
        if (fault is "credential") { capture.Should().Throw<ConversationCredentialException>(); }
        else { capture.Should().Throw<InvalidDataException>(); }
        if (fault is not "count") { turn.SnapshotObservations().Should().BeNull(); }
    }
}
