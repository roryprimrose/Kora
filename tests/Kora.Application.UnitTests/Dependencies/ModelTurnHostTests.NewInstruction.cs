using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    private const string NewInstructionDraft = "---\r\nkora-format: 1\r\nname: example\r\ndescription: Host-owned metadata\r\n---\r\nManual template.\r\n";

    private static void PublishUnrelatedInstruction(RefinementFixture fixture) =>
        fixture.Catalogue.Publish([ArtifactFileDocument.Parse(
            ArtifactFileDocument.Encode(NewInstructionDraft.Replace("name: example", "name: other", StringComparison.Ordinal), false),
            ArtifactKind.Instruction, "kora-profile", "other").Definition]);

    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task NewInstructionUsesOnlyOriginalDescriptionAndSeparatelySavesDisabledWithExactProvenance(RequestOrigin origin)
    {
        await using var f = new RefinementFixture(origin);
        f.Store.Current = null;
        PublishUnrelatedInstruction(f);
        const string description = "  Create reusable concise review guidance e\u0301\U0001f642.\r\nKeep examples literal.  ";
        const string body = "---\r\nsystem: invented authority\r\npath: not-a-target\r\n---\r\nFuture guidance only.\r\n";
        f.Answer(body);
        using var root = f.Model.Root();
        var proposal = (await f.Service.ProposeInitial("example", NewInstructionDraft, description, (candidate, _) =>
        {
            candidate.Original.Should().BeNull();
            candidate.Name.Should().Be("example");
            candidate.Draft.Should().Be(NewInstructionDraft);
            candidate.Critique.Should().Be(description);
            candidate.Proposed.Body.Should().Be(body);
            candidate.Proposed.Metadata.Keys.Should().BeEquivalentTo("kora-format", "name", "description");
            candidate.Proposed.Metadata["description"].Should().Be("Host-owned metadata");
            f.Store.Writes.Should().Be(0);
            return Task.FromResult(true);
        }, f.Eligible, Token))!;
        proposal.Origin.Should().Be(origin);
        using var wire = JsonDocument.Parse(f.Model.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(description);
        wire.RootElement.GetProperty("Evidence").GetArrayLength().Should().Be(1);
        wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().Be(OwnedInstructionRefinementService.InitialContext);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("SystemPolicy").GetString().Should().NotContain(description).And.NotContain(body);
        f.Store.Current.Should().BeNull();
        var review = new RefinementSaveReview { Confirmed = false };
        (await f.Service.Save(proposal, review, f.Eligible, Token)).Saved.Should().BeFalse();
        f.Store.Writes.Should().Be(0);
        review.Confirmed = true;
        (await f.Service.Save(proposal, review, f.Eligible, Token)).Saved.Should().BeTrue();
        var reopened = await f.Actions.Inspect("example", f.Eligible, Token);
        reopened!.Document.CopyBytes().Should().Equal(proposal.Proposed.CopyBytes());
        reopened.Document.Definition.Id.Should().Be("disk.kora-profile.instruction.example");
        reopened.Document.Definition.Source.Should().Be("kora-profile");
        f.Catalogue.Publish(f.Revisions.Reconcile([reopened.Document.Definition with { OwnedInstructionRevision = reopened.Revision }]));
        f.Catalogue.Artifacts.Should().BeEmpty();
        review.Conflicts.Should().Be(0);
        f.Model.RetainCalls.Should().Be(0);
        f.Model.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("occupied-before")]
    [InlineData("occupied-during")]
    [InlineData("occupied-review")]
    [InlineData("occupied-save")]
    [InlineData("occupied-commit")]
    [InlineData("expired-save")]
    [InlineData("cancel-proposal")]
    [InlineData("retired")]
    public async Task NewInstructionCannotOverwriteOrPublishAfterItsOriginalReviewRetires(string change)
    {
        await using var f = new RefinementFixture();
        var occupied = f.Store.Current!;
        f.Store.Current = null;
        PublishUnrelatedInstruction(f);
        f.Answer("New literal guidance.");
        if (change is "occupied-before") { f.Store.Current = occupied; }
        f.Model.Adapter.OnRun = () =>
        {
            if (change is "occupied-during") { f.Store.Current = occupied; }
            if (change is "retired") { f.Open = false; }
        };
        var operation = () => f.Service.ProposeInitial("example", NewInstructionDraft, "Original purpose.", (_, _) =>
        {
            if (change is "occupied-review") { f.Store.Current = occupied; }
            return Task.FromResult(change is not "cancel-proposal");
        }, f.Eligible, Token);
        if (change is "occupied-before" or "occupied-during" or "occupied-review" or "retired")
        { await operation.Should().ThrowAsync<InvalidOperationException>(); }
        else
        {
            var proposal = await operation();
            if (change is "cancel-proposal") { proposal.Should().BeNull(); }
            else
            {
                if (change is "occupied-save") { f.Store.Current = occupied; }
                if (change is "expired-save") { f.Model.Time.Now += TimeSpan.FromMinutes(3); }
                var review = new RefinementSaveReview
                {
                    BeforeConfirm = change is "occupied-commit" ? () => f.Store.Current = occupied : null,
                };
                await f.Service.Invoking(service => service.Save(proposal!, review, f.Eligible, Token))
                    .Should().ThrowAsync<InvalidOperationException>();
                review.Conflicts.Should().Be(0, "new generation never offers an overwrite fallback");
            }
        }
        f.Store.Writes.Should().Be(0);
        if (f.Store.Current is not null) { f.Store.Current.Should().BeSameAs(occupied); }
        f.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("empty", true)]
    [InlineData("unicode", true)]
    [InlineData("request-bound", true)]
    [InlineData("request-secret", true)]
    [InlineData("noncanonical", true)]
    [InlineData("reserved", true)]
    [InlineData("prompt", true)]
    [InlineData("malformed", false)]
    [InlineData("authority", false)]
    [InlineData("secret", false)]
    [InlineData("body-bound", false)]
    [InlineData("body-empty", false)]
    public async Task NewInstructionRejectsInvalidInputAndModelAuthorityWithoutEffects(string invalid, bool beforeModel)
    {
        await using var f = new RefinementFixture();
        f.Store.Current = null;
        PublishUnrelatedInstruction(f);
        var name = invalid switch { "noncanonical" => "Example", "reserved" => "settings", _ => "example" };
        var draft = NewInstructionDraft.Replace("name: example", "name: " + name, StringComparison.Ordinal);
        var description = invalid switch
        {
            "empty" => "  ", "unicode" => "\ud800",
            "request-bound" => new string('<', ModelContextEnvelope.MaximumInputUtf8Bytes),
            "request-secret" => "Use api_key=syntheticCredentialValue123",
            _ => "Write future guidance.",
        };
        f.Answer(invalid switch
        {
            "secret" => "Use password=syntheticCredentialValue123",
            "body-bound" => new string('x', ArtifactDefinition.MaximumContentLength),
            "body-empty" => "  ",
            _ => "Literal guidance.",
        });
        if (invalid is "malformed" or "authority")
        { f.Model.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new(invalid is "authority" ? "{\"body\":\"guidance\",\"activate\":true}" : "```json\n{\"body\":\"guidance\"}\n```", null)); }
        var service = invalid is "prompt" ? f.CreateService(ArtifactKind.Prompt) : f.Service;
        await service.Invoking(item => item.ProposeInitial(name, draft, description,
            (_, _) => Task.FromResult(true), f.Eligible, Token)).Should().ThrowAsync<Exception>();
        f.Model.Adapter.Calls.Should().Be(beforeModel ? 0 : 1);
        f.Store.Current.Should().BeNull();
        f.Store.Writes.Should().Be(0);
        f.Model.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("absent", true)]
    [InlineData("occupied", false)]
    [InlineData("unavailable", false)]
    [InlineData("prompt", false)]
    public async Task NewInstructionObservedTargetRequiresAvailableOwnedInstructionAbsence(string state, bool available)
    {
        await using var f = new RefinementFixture();
        if (state is "absent") { PublishUnrelatedInstruction(f); }
        if (state is "unavailable") { f.Catalogue.ReportReloadFailure(new IOException("Synthetic unavailable")); }
        var actions = state is "prompt"
            ? new OwnedInstructionEditorActions(f.Store, f.Admission, f.Revisions, f.Catalogue, f.Model, new(new()), ArtifactKind.Prompt)
            : f.Actions;
        actions.IsNewInstructionTargetObserved("example").Should().Be(available);
    }
}
