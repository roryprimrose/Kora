using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Application.UnitTests.Definitions;

public sealed class CustomizationTests
{
    [Fact]
    public void Editor_candidates_have_one_way_retirement_and_reference_detection_is_conservative()
    {
        var live = true;
        var context = new CustomizationVoiceContext(CustomizationKind.Instruction, CustomizationScope.Kora,
            "example", "example.instructions.md", false, false);
        var target = new CustomizationVoiceTarget(context, () => live);
        target.Context.Should().BeSameAs(context);
        target.IsCurrent.Should().BeTrue();
        live = false;
        target.IsCurrent.Should().BeFalse();
        live = true;
        target.Retire();
        target.Retire();
        target.IsCurrent.Should().BeFalse();
        foreach (var phrase in new[] { "this editor", "this artifact", "this instruction", "this skill", "this agent", "this MCP server", "this prompt" })
        { CustomizationCommand.ReferencesEditor("Please discuss " + phrase.ToUpperInvariant()).Should().BeTrue(); }
        CustomizationCommand.ReferencesEditor("an unrelated request").Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"intent\":\"edit\"}", CustomizationVoiceIntent.Edit)]
    [InlineData("{\"intent\":\"discuss\"}", CustomizationVoiceIntent.Discuss)]
    [InlineData("{\"intent\":\"clarify\"}", CustomizationVoiceIntent.Clarify)]
    public void Voice_interpretation_has_only_three_bounded_outcomes(string json, CustomizationVoiceIntent expected) =>
        CustomizationAuthoringService.ParseVoiceIntent(json).Should().Be(expected);

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"intent\":\"edit\",\"path\":\"elsewhere\"}")]
    [InlineData("{\"intent\":\"edit\",\"intent\":\"edit\"}")]
    [InlineData("{\"unknown\":\"edit\"}")]
    [InlineData("{\"intent\":true}")]
    [InlineData("{\"intent\":\"save\"}")]
    [InlineData("{\"intent\":null}")]
    public void Invalid_or_authority_widening_voice_proposals_are_not_routing(string json) =>
        ((Action)(() => CustomizationAuthoringService.ParseVoiceIntent(json))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("Show skills", "List", CustomizationKind.Skill, null)]
    [InlineData("Show me example instruction", "Select", CustomizationKind.Instruction, "example")]
    [InlineData("Kora, Edit example skill", "Select", CustomizationKind.Skill, "example")]
    [InlineData("Disable example agent", "Disable", CustomizationKind.Agent, "example")]
    [InlineData("Enable example MCP server", "Enable", CustomizationKind.McpServer, "example")]
    [InlineData("Add an instruction named example", "Create", CustomizationKind.Instruction, "example")]
    [InlineData("Add a skill", "Create", CustomizationKind.Skill, null)]
    [InlineData("customizations", "List", null, null)]
    [InlineData("show customizations", "List", null, null)]
    [InlineData("open customizations", "List", null, null)]
    [InlineData("show my agents", "List", CustomizationKind.Agent, null)]
    [InlineData("Add a skill that explains logs", "Create", CustomizationKind.Skill, null)]
    public void Whole_original_commands_select_the_shared_management_operation(string input, string operation, CustomizationKind? kind, string? name)
    {
        var command = CustomizationCommand.Parse(input, "Kora")!;
        command.Should().NotBeNull();
        command.Operation.Should().Be(operation); command.Kind.Should().Be(kind); command.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("Please show skills and delete them")]
    [InlineData("Edit example")]
    [InlineData("arbitrary artifact body")]
    public void Unmatched_text_is_not_a_management_command(string input) =>
        CustomizationCommand.Parse(input, "Kora").Should().BeNull();

    [Fact]
    public void Use_disable_survives_recreation_and_is_independent_for_each_source()
    {
        var state = new LocalSharedSkillPreferencesTests.Store();
        var first = new ArtifactUsePreferences(state);
        first.SetDisabled("bundled.example", true);
        var reloaded = new ArtifactUsePreferences(state);
        reloaded.IsDisabled("bundled.example").Should().BeTrue();
        reloaded.IsDisabled("other.example").Should().BeFalse();
        reloaded.SetDisabled("bundled.example", false);
        new ArtifactUsePreferences(state).IsDisabled("bundled.example").Should().BeFalse();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"Version\":2,\"Disabled\":[]}")]
    [InlineData("{\"Version\":1,\"Disabled\":[],\"Disabled\":[]}")]
    [InlineData("{\"Version\":1,\"Disabled\":[\"a\",\"a\"]}")]
    [InlineData("{\"Version\":1,\"Disabled\":[\"../x\"]}")]
    [InlineData("{\"Version\":1,\"Disabled\":[false]}")]
    [InlineData("{\"Version\":1,\"Disabled\":null}")]
    [InlineData("{\"Version\":\"one\",\"Disabled\":[]}")]
    [InlineData("{\"Version\":1.5,\"Disabled\":[]}")]
    public void Malformed_control_state_fails_closed(string text) =>
        ((Action)(() => _ = new ArtifactUsePreferences(new LocalSharedSkillPreferencesTests.Store { Text = text })))
            .Should().Throw<InvalidDataException>();

    [Fact]
    public void Unconfirmed_persistence_and_missing_observed_state_close_all_use()
    {
        var state = new LocalSharedSkillPreferencesTests.Store();
        var use = new ArtifactUsePreferences(state);
        use.SetDisabled("example", true);
        state.Text = null;
        ((Action)(() => use.SetDisabled("example", false))).Should().Throw<InvalidDataException>();
        use.IsDisabled("unrelated").Should().BeTrue();
        ((Action)(() => use.SetDisabled("example", false))).Should().Throw<InvalidOperationException>();
        var failed = new LocalSharedSkillPreferencesTests.Store { FailWrite = true };
        var unconfirmed = new ArtifactUsePreferences(failed);
        ((Action)(() => unconfirmed.SetDisabled("example", false))).Should().Throw<IOException>();
        unconfirmed.IsDisabled("unrelated").Should().BeTrue();
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("bytes")]
    [InlineData("readback")]
    public void Use_control_bounds_and_unconfirmed_readback_fail_closed(string failure)
    {
        var ids = Enumerable.Range(0, failure is "entries" ? 1024 : failure is "bytes" ? 1000 : 0)
            .Select(index => failure is "bytes"
                ? index.ToString(System.Globalization.CultureInfo.InvariantCulture).PadRight(128, 'a')
                : index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var state = new LocalSharedSkillPreferencesTests.Store { Text = JsonSerializer.Serialize(new { Version = 1, Disabled = ids }) };
        var use = new ArtifactUsePreferences(state);
        if (failure is "readback") { state.AfterWrite = () => state.Text = null; }
        Action write = () => use.SetDisabled(failure is "bytes" ? new string('n', 128) : "new", true);
        write.Should().Throw<Exception>();
        use.IsDisabled("unrelated").Should().BeTrue();
    }

    [Fact]
    public void Actual_local_use_control_store_reloads_independent_exclusions()
    {
        var root = Path.Combine(Path.GetTempPath(), "Kora-customization-use", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new Paths(root);
            var use = new ArtifactUsePreferences(paths);
            use.SetDisabled("example", true);
            new ArtifactUsePreferences(paths).IsDisabled("example").Should().BeTrue();
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); } }
    }

    [Fact]
    public void Fatal_store_failures_are_not_reinterpreted_as_success_or_swallowed()
    {
#pragma warning disable CA2201, MA0012 // Synthetic fatal-boundary regression; no memory pressure is induced.
        var state = new LocalSharedSkillPreferencesTests.Store { AfterWrite = () => throw new OutOfMemoryException("Synthetic fatal failure") };
#pragma warning restore CA2201, MA0012
        var use = new ArtifactUsePreferences(state);
        ((Action)(() => use.SetDisabled("example", true))).Should().Throw<OutOfMemoryException>();
    }

    [Fact]
    public void Unexpected_store_failures_close_all_use_before_propagating()
    {
        var state = new LocalSharedSkillPreferencesTests.Store { AfterWrite = () => throw new ArgumentException("Unexpected storage failure") };
        var use = new ArtifactUsePreferences(state);
        var notifications = 0;
        use.Changed += (_, _) => notifications++;
        ((Action)(() => use.SetDisabled("example", true))).Should().Throw<ArgumentException>();
        use.IsDisabled("unrelated").Should().BeTrue();
        notifications.Should().Be(1, "runtime consumers must retire pending use when persistence is uncertain");
    }
    [Fact]
    public void Model_proposals_preserve_bom_and_validate_the_complete_bounded_file_set()
    {
        var source = new CustomizationSource("test", CustomizationKind.Instruction, CustomizationScope.Kora, "example", "source", "example.instructions.md");
        var original = new CustomizationFile(source.PrimaryFile, ArtifactFileDocument.Encode(CustomizationLayout.Template(source.Kind, source.Name), true));
        var answer = JsonSerializer.Serialize(new { explanation = "Updated guidance", files = new[] { new { name = source.PrimaryFile, text = original.Text + "Change." } } });
        var result = CustomizationAuthoringService.Parse(source, [original], answer);
        result.Explanation.Should().Be("Updated guidance");
        result.Files.Single().HasBom.Should().BeTrue();
        result.Files.Single().Text.Should().EndWith("Change.");
        ((Action)(() => CustomizationAuthoringService.Parse(source, [original], "{}"))).Should().Throw<InvalidDataException>();
        ((Action)(() => CustomizationAuthoringService.Parse(source, [original], "{\"explanation\":\"x\",\"files\":[]}"))).Should().Throw<InvalidDataException>();
        ((Action)(() => CustomizationAuthoringService.Parse(source, [original],
            "{\"explanation\":\"x\",\"files\":[{\"name\":\"..\\\\outside\",\"text\":\"x\"}]}"))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"explanation\":false,\"files\":[]}")]
    [InlineData("{\"explanation\":\"x\",\"files\":false}")]
    [InlineData("{\"explanation\":\"x\",\"files\":[{\"name\":null,\"text\":\"x\"}]}")]
    [InlineData("{\"explanation\":\"x\",\"files\":[{\"name\":\"SKILL.md\",\"text\":null}]}")]
    public void Invalid_model_proposal_shapes_never_produce_a_partial_file_set(string answer)
    {
        var source = new CustomizationSource("test", CustomizationKind.Skill, CustomizationScope.User, "example", "source", "SKILL.md");
        ((Action)(() => CustomizationAuthoringService.Parse(source, [], answer))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Model_file_additions_are_supported_but_count_bounded()
    {
        var source = new CustomizationSource("test", CustomizationKind.Skill, CustomizationScope.User, "example", "source", "SKILL.md");
        var primary = new CustomizationFile("SKILL.md", ArtifactFileDocument.Encode(CustomizationLayout.Template(source.Kind, source.Name), false));
        string Answer(int count) => JsonSerializer.Serialize(new { explanation = "x", files = Enumerable.Range(0, count)
            .Select(index => new { name = index == 0 ? "SKILL.md" : index + ".md", text = index == 0 ? primary.Text : "Added" }) });
        CustomizationAuthoringService.Parse(source, [primary], Answer(2)).Files.Count.Should().Be(2);
        ((Action)(() => CustomizationAuthoringService.Parse(source, [primary], Answer(33)))).Should().Throw<InvalidDataException>();
    }

    private sealed record Paths(string LocalRoot) : Kora.Core.Dependencies.IApplicationDataPaths
    { public string RoamingRoot => LocalRoot; }
}
