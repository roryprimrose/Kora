using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Application.Agents;
using Kora.Application.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalAgentDescriptionGeneratesOnlyInertToolFreeFieldsWithoutRetention(bool voice)
    {
        using var f = new Fixture();
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        await using var runner = AuthoringRunner(f, f.PolicyHost);
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new("{\"description\":\"Explain supplied text\",\"body\":\"Genuine fake model guidance.\"}", null));
        var service = new CustomizationAuthoringService(admission, runner, f.Time);
        var origin = voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(origin),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        const string request = "  Explain only the supplied text e\u0301\U0001f642.  ";
        var result = await service.ProposeAgent("example", "", request, () => true, Token);
        result.Document.Body.Should().Be("Genuine fake model guidance.");
        result.Profile.Tools.Should().BeEmpty();
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(request);
        wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().BeEmpty();
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin.Should().Be(origin);
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(0);
        f.Messages.Should().NotContain(message => message.Contains(request, StringComparison.Ordinal)
            || message.Contains("Genuine fake model guidance", StringComparison.Ordinal));
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task OriginalAgentGenerationCancellationJoinsActualLocalModelAndNeverRetainsOutput()
    {
        using var f = new Fixture();
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        await using var runner = AuthoringRunner(f, f.PolicyHost);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        f.Adapter.OnRun = cancellation.Cancel;
        var service = new CustomizationAuthoringService(admission, runner, f.Time);
        await service.Invoking(authoring => authoring.ProposeAgent("example", "", "Explain supplied text", () => true, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(0);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Voice_routing_supplies_only_reviewed_metadata_and_original_voice_origin_to_one_local_tool_free_turn()
    {
        using var f = new Fixture();
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        await using var runner = AuthoringRunner(f, f.PolicyHost);
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate, new("{\"intent\":\"edit\"}", null));
        var service = new CustomizationAuthoringService(admission, runner, f.Time);
        using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var result = await service.Interpret(new(CustomizationKind.Skill, CustomizationScope.User, "example", "SKILL.md", false, false),
            "make this clearer", () => true, Token);
        result.Should().Be(CustomizationVoiceIntent.Edit);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("make this clearer");
        wire.RootElement.GetRawText().Should().Contain("SKILL.md").And.NotContain("draft files");
        Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.RetainCalls.Should().Be(0);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(CustomizationAuthoringMode.Edit, @"references\notes.md")]
    [InlineData(CustomizationAuthoringMode.Discuss, @"references\notes.md")]
    [InlineData(CustomizationAuthoringMode.FromRequest, null)]
    [InlineData(CustomizationAuthoringMode.Edit, "SKILL.md")]
    public async Task Authoring_model_receives_reviewed_component_and_resolved_mode_without_rewriting_original_voice_input(
        CustomizationAuthoringMode mode, string? component)
    {
        using var f = new Fixture();
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        await using var runner = AuthoringRunner(f, f.PolicyHost);
        var source = new CustomizationSource("file.example", CustomizationKind.Skill, CustomizationScope.Kora, "example", "source", "SKILL.md");
        CustomizationFile[] files = [
            new(source.PrimaryFile, ArtifactFileDocument.Encode(CustomizationLayout.Template(source.Kind, source.Name), false)),
            new(@"references\notes.md", ArtifactFileDocument.Encode("An old example.", false)),
        ];
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new(JsonSerializer.Serialize(new { explanation = "Discuss the edit.", files = files.Select(file => new { name = file.Name, text = file.Text }) }), null));
        var service = new CustomizationAuthoringService(admission, runner, f.Time);
        using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            Kora.Core.Diagnostics.HostActivityLayer.Application, Kora.Core.Diagnostics.HostOperation.Request);
        var result = await service.Refine(source, files, new(component, mode), "remove the old example from this file", () => true, Token);
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("remove the old example from this file");
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        using var evidence = JsonDocument.Parse(wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString()!);
        evidence.RootElement.GetProperty("component").GetString().Should().Be(component);
        evidence.RootElement.GetProperty("mode").GetString().Should().Be(mode.ToString());
        evidence.RootElement.GetProperty("files").GetArrayLength().Should().Be(2);
        evidence.RootElement.GetProperty("files")[1].GetProperty("text").GetString().Should().Be(files[1].Text);
        result.Files.Select(file => file.Digest).Should().Equal(files.Select(file => file.Digest));
        Kora.Core.Diagnostics.HostActivity.RequireCurrent().Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.Adapter.Calls.Should().Be(1);
        f.RetainCalls.Should().Be(0);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData((CustomizationAuthoringMode)(-1), "SKILL.md")]
    [InlineData(CustomizationAuthoringMode.Edit, @"references\missing.md")]
    [InlineData(CustomizationAuthoringMode.Discuss, "skill.md")]
    public async Task Invalid_authoring_scope_is_rejected_before_any_model_disclosure(CustomizationAuthoringMode mode, string component)
    {
        using var f = new Fixture();
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        await using var runner = AuthoringRunner(f, f.PolicyHost);
        var source = new CustomizationSource("file.example", CustomizationKind.Skill, CustomizationScope.Kora, "example", "source", "SKILL.md");
        CustomizationFile[] files = [new(source.PrimaryFile, ArtifactFileDocument.Encode(CustomizationLayout.Template(source.Kind, source.Name), false))];
        var service = new CustomizationAuthoringService(admission, runner, f.Time);
        Action action = () => _ = service.Refine(source, files, new(component, mode), "change this file", () => true, Token);
        action.Should().Throw<InvalidDataException>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Fact]
    public async Task Universal_exclusion_epoch_retires_scoped_instruction_review_even_after_reenable()
    {
        using var f = new Fixture();
        var use = new ArtifactUsePreferences(new Kora.Application.UnitTests.Configuration.LocalSharedSkillPreferencesTests.Store());
        var (broker, catalogue, _) = InstructionBroker(f, new InstructionSource(), usePolicy: use);
        using var ownedCatalogue = catalogue;
        var source = catalogue.Artifacts.Single();
        var review = await broker.ReviewAsync(new(f.Session, null), source, "Request", () => true, Token);
        use.SetDisabled(source.Id, true);
        use.SetDisabled(source.Id, false);
        review.IsCurrent().Should().BeFalse();
        await f.ConversationService(f.PolicyHost, execution: f.PolicyHost).Invoking(service =>
            service.SubmitInstructionAsync(review, () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Adapter.Calls.Should().Be(0);
        f.RetainCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Agent_exclusion_uses_resolved_source_identity_and_disable_reenable_retires_review(bool initiallyDisabled)
    {
        using var f = new Fixture();
        var reader = new AgentReader();
        var policy = new ArtifactUsePreferences(new Kora.Application.UnitTests.Configuration.LocalSharedSkillPreferencesTests.Store());
        var identity = ArtifactUseIdentity.File(reader.Metadata.CanonicalPath);
        policy.SetDisabled(identity, initiallyDisabled);
        var service = new SessionConversationService(f, f,
            new SessionWorkspaceService(f, new(f), f, Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionWorkspaceService>.Instance),
            f, f.PolicyHost, f.Time, Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionConversationService>.Instance);
        await using var workflow = new BoundedAgentWorkflow(reader, f, f, service, f.PolicyHost, f, f.Time, usePolicy: policy);
        using var root = f.Root();
        var action = () => workflow.ReviewAsync("relative-alias.agent.md", new(f.Session, null), "task",
            RequestOrigin.LocalUi, () => true, Token);
        if (initiallyDisabled) { await action.Should().ThrowAsync<InvalidOperationException>(); }
        else
        {
            using var review = await action();
            policy.SetDisabled(identity, true);
            policy.SetDisabled(identity, false);
            await workflow.Invoking(value => value.InvokeAsync(review, Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        reader.Released.Should().BeTrue();
        f.RetainCalls.Should().Be(0);
        f.Adapter.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("unavailable")]
    [InlineData("failure")]
    [InlineData("malformed")]
    [InlineData("revoked")]
    [InlineData("throw")]
    public async Task Customization_authoring_uses_one_local_tool_free_turn_and_quiesces_before_returning(string outcome)
    {
        using var f = new Fixture();
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        var source = new CustomizationSource("file.example", CustomizationKind.Instruction, CustomizationScope.Kora,
            "example", "source", "example.instructions.md");
        CustomizationFile[] files = [new(source.PrimaryFile, ArtifactFileDocument.Encode(
            CustomizationLayout.Template(source.Kind, source.Name), false))];
        var text = outcome is "malformed" ? "{}" : JsonSerializer.Serialize(new
        {
            explanation = "Proposed",
            files = new[] { new { name = source.PrimaryFile, text = files[0].Text + "Refined." } },
        });
        f.Adapter.Reply = new(outcome is "failure" ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Succeeded,
            ModelProviderSelection.OllamaCandidate, new(text, null));
        var eligible = true;
        f.Adapter.OnRun = () =>
        {
            if (outcome is "revoked") { eligible = false; }
            if (outcome is "throw") { throw new IOException("Provider failed"); }
        };
        var host = outcome is "unavailable" ? f.Production() : f.PolicyHost;
        await using var runner = AuthoringRunner(f, host);
        var service = new CustomizationAuthoringService(admission, runner, f.Time);
        var action = () => service.Refine(source, files, new(source.PrimaryFile), "Explain and refine this draft", () => eligible, Token);
        if (outcome is "success")
        {
            var result = await action();
            result.Explanation.Should().Be("Proposed");
            result.Files.Single().Text.Should().EndWith("Refined.");
            using var wire = JsonDocument.Parse(f.Adapter.Wire!);
            wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("Explain and refine this draft");
        }
        else { await action.Should().ThrowAsync<Exception>(); }
        host.IsQuiescent.Should().BeTrue();
        f.Adapter.Calls.Should().Be(outcome is "unavailable" ? 0 : 1);
        files[0].Text.Should().NotContain("Refined.");
        f.RetainCalls.Should().Be(0);
        f.Messages.Should().NotContain(message => message.Contains("Explain and refine", StringComparison.Ordinal));
        await service.Invoking(authoring => authoring.Refine(source, files, new(null), " ", () => true, Token))
            .Should().ThrowAsync<InvalidDataException>();
    }

    private static LocalContextTurnWorkflow AuthoringRunner(Fixture f, ModelTurnHost host) =>
        new(f, f, new SessionWorkspaceService(f, new(f), f, NullLogger<SessionWorkspaceService>.Instance),
            new(f), f, host, host, new AuthoringAudit(f), NullLogger<LocalContextTurnWorkflow>.Instance);

    private sealed class AuthoringAudit(Fixture f) : Kora.Core.Auditing.ISecurityAuditLog
    {
        public void Write(Kora.Core.Auditing.SecurityAuditEvent auditEvent) => f.Audits.Add(auditEvent);
    }

    private sealed partial class Fixture : IInstructionControlSessionStore
    {
        public ValueTask<Kora.Core.Authorization.WorkSessionAuthorization> CreateInstructionSession(
            HostRequest request, Func<bool> admitted, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!admitted()) { throw new InvalidOperationException("Closed"); }
            Session = new(request.SessionId, new(1), true);
            return ValueTask.FromResult(Session);
        }

        public ValueTask<T> WithInstructionSession<T>(HostRequest request, HostRevision generation,
            Func<T> operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Session.IsActive || Session.SessionId != request.SessionId || Session.Generation != generation)
            { throw new InvalidOperationException("Session retired"); }
            return ValueTask.FromResult(operation());
        }
    }
}
