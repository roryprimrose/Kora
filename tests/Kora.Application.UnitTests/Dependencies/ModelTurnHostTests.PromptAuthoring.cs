using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Definitions;
using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task PromptAuthoringDisclosesExactDescriptionAndSourceButOnlyHostChosenLiteralsCanReturn(RequestOrigin origin)
    {
        await using var local = new RefinementFixture(origin);
        var f = local.Model;
        using var root = f.Root();
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        var source = new CustomizationSource("file.example", CustomizationKind.Prompt, CustomizationScope.Kora,
            "example", "owned", "example.prompt.md");
        var file = new CustomizationFile(source.PrimaryFile, ArtifactFileDocument.Encode(CustomizationLayout.Template(source.Kind, source.Name), true));
        const string description = "  Reusable request to summarize topic for audience.\r\nDo not run it.  ";
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new("{\"body\":\"Summarize ${input:topic} for ${input:audience}.\"}", null));
        var service = new CustomizationAuthoringService(admission, local.Runner, f.Time);
        var sourceChecks = 0;
        var result = await service.Refine(source, [file], new(source.PrimaryFile, RequiredPromptInputs: "topic,audience"),
            description, () => true, Token, _ => { sourceChecks++; return Task.CompletedTask; });
        sourceChecks.Should().BeGreaterThan(1, "the source is checked at the actual model consumer boundary too");
        result.Files.Single().HasBom.Should().BeTrue();
        using var wire = JsonDocument.Parse(f.Adapter.Wire!);
        wire.RootElement.GetProperty("UserRequest").GetString().Should().Be(description);
        wire.RootElement.GetProperty("Tools").GetArrayLength().Should().Be(0);
        wire.RootElement.GetProperty("SystemPolicy").GetString().Should().NotContain(description).And.NotContain(file.Text);
        using var evidence = JsonDocument.Parse(wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString()!);
        evidence.RootElement.GetProperty("source").GetString().Should().Be(file.Text);
        evidence.RootElement.GetProperty("requiredInputs").EnumerateArray().Select(item => item.GetString()).Should().Equal("topic", "audience");
        file.Text.Should().NotContain("Summarize");
        f.RetainCalls.Should().Be(0);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing-declaration")]
    [InlineData("profile")]
    [InlineData("legacy-fields")]
    [InlineData("invalid-field")]
    [InlineData("source-before")]
    [InlineData("expired")]
    [InlineData("source-after")]
    [InlineData("unsupported")]
    public async Task PromptAuthoringRejectsStaleUnsupportedOrUndisclosedInputsWithoutRetainingOrWriting(string change)
    {
        await using var local = new RefinementFixture();
        var f = local.Model;
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        var source = new CustomizationSource("file.example", CustomizationKind.Prompt,
            change is "profile" ? CustomizationScope.User : CustomizationScope.Kora, "example", "owned", "example.prompt.md");
        var text = CustomizationLayout.Template(source.Kind, source.Name);
        if (change is "legacy-fields") { text = text.Replace("kora-format: 2", "kora-format: 1", StringComparison.Ordinal); }
        var file = new CustomizationFile(source.PrimaryFile, ArtifactFileDocument.Encode(text, false));
        var context = new CustomizationAuthoringContext(source.PrimaryFile, RequiredPromptInputs:
            change is "missing-declaration" ? null : change is "invalid-field" ? "a,a" : "topic");
        f.Adapter.Reply = new(ModelTurnOutcome.Succeeded, ModelProviderSelection.OllamaCandidate,
            new(change is "unsupported" ? "{\"unsupported\":\"tools\"}" : "{\"body\":\"Request ${input:topic}\"}", null));
        var sourceCurrent = change is not "source-before";
        f.Adapter.OnRun = () =>
        {
            if (change is "expired") { f.Time.Now += TimeSpan.FromMinutes(3); }
            if (change is "source-after") { sourceCurrent = false; }
        };
        var service = new CustomizationAuthoringService(admission, local.Runner, f.Time);
        var operation = () => service.Refine(source, [file], context, "Describe a repeatable request", () => true, Token,
            _ => sourceCurrent ? Task.CompletedTask : throw new InvalidOperationException("Source changed"));
        await operation.Should().ThrowAsync<Exception>();
        f.Adapter.Calls.Should().Be(change is "expired" or "source-after" or "unsupported" ? 1 : 0);
        f.RetainCalls.Should().Be(0);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData("missing-runner")]
    [InlineData("empty")]
    [InlineData("unavailable")]
    [InlineData("cancel")]
    [InlineData("metadata-only")]
    public async Task PromptAuthoringUsesTheSameLocalRunnerForVoiceMetadataAndTruthfulFailure(string change)
    {
        await using var local = new RefinementFixture(RequestOrigin.ActivatedVoice);
        var f = local.Model;
        await using var admission = new InstructionControlAdmission(f, f, new(f));
        using var root = f.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var source = new CustomizationSource("file.example", CustomizationKind.Prompt, CustomizationScope.Kora,
            "example", "owned", "example.prompt.md");
        var file = new CustomizationFile(source.PrimaryFile, ArtifactFileDocument.Encode(CustomizationLayout.Template(source.Kind, source.Name), false));
        f.Adapter.Reply = new(change is "unavailable" ? ModelTurnOutcome.Unavailable : ModelTurnOutcome.Succeeded,
            ModelProviderSelection.OllamaCandidate, new(change is "metadata-only" ? "{\"intent\":\"edit\"}" : "{\"body\":\"Request\"}", null));
        if (change is "cancel") { f.Adapter.OnRun = cancellation.Cancel; }
        var service = new CustomizationAuthoringService(admission,
            change is "missing-runner" ? null! : local.Runner, f.Time);
        if (change is "metadata-only")
        {
            (await service.Interpret(new(CustomizationKind.Prompt, CustomizationScope.Kora, "example", source.PrimaryFile, false, false),
                "Make this request clearer", () => true, cancellation.Token)).Should().Be(CustomizationVoiceIntent.Edit);
            using var wire = JsonDocument.Parse(f.Adapter.Wire!);
            wire.RootElement.GetProperty("UserRequest").GetString().Should().Be("Make this request clearer");
            wire.RootElement.GetProperty("Evidence")[0].GetProperty("Content").GetString().Should().NotContain(file.Text);
        }
        else
        {
            var operation = () => service.Refine(source, [file], new(source.PrimaryFile, RequiredPromptInputs: ""),
                change is "empty" ? " " : "Repeatable request", () => true, cancellation.Token);
            await operation.Should().ThrowAsync<Exception>();
        }
        f.Adapter.Calls.Should().Be(change is "missing-runner" or "empty" ? 0 : 1);
        f.RetainCalls.Should().Be(0);
        f.PolicyHost.IsQuiescent.Should().BeTrue();
    }
}
