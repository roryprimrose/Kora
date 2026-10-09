using System.Text;

using AwesomeAssertions;

using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class SharedSkillTests
{
    private static readonly Guid SourceId = Guid.NewGuid();
    private const string Valid = "---\nname: inspect-example\nversion: 1.0.0\ndescription: Inspect a selected example\n---\n# Instructions\nExplain the supplied text, without tools.";

    [Fact]
    public void Consent_identity_is_exact_metadata_not_a_mutable_root_name_or_old_snapshot()
    {
        var source = new SharedSkillSource(SourceId, ".agents\\skills", new string('a', 48));
        new SharedSkillSource(SourceId, source.ProfileRelativeRoot, source.DirectoryIdentity).Should().Be(source);
        new SharedSkillSource(Guid.NewGuid(), source.ProfileRelativeRoot, source.DirectoryIdentity).Should().NotBe(source);
        new SharedSkillSource(SourceId, source.ProfileRelativeRoot, new string('b', 48)).Should().NotBe(source);
        new SharedSkillSource(SourceId, ".AGENTS\\skills", source.DirectoryIdentity).Should().NotBe(source);
        var old = Parse(Valid);
        var newSource = new SharedSkillSource(Guid.NewGuid(), source.ProfileRelativeRoot, source.DirectoryIdentity);
        SharedSkillSnapshot.Parse(newSource.Id, old.RelativeFile, old.Bytes.ToArray(), false)
            .SourceQualifiedIdentity.Should().NotBe(old.SourceQualifiedIdentity);
        old.IsAvailableForInvocation.Should().BeFalse();
    }

    [Fact]
    public void Compatible_instruction_snapshot_preserves_exact_bytes_and_has_no_authority()
    {
        var bytes = Encoding.UTF8.GetBytes("\uFEFF" + Valid.Replace("\n", "\r\n", StringComparison.Ordinal));
        var expected = bytes.ToArray();
        var snapshot = SharedSkillSnapshot.Parse(SourceId, "example\\SKILL.md", bytes, false);
        bytes[5] = 0;
        var copy = snapshot.Bytes.ToArray();
        copy[6] = 0;
        snapshot.Bytes.ToArray().Should().Equal(expected);
        snapshot.Text.Should().Be(Encoding.UTF8.GetString(expected));
        snapshot.Name.Should().Be("inspect-example");
        snapshot.Version.Should().Be("1.0.0");
        snapshot.Description.Should().Be("Inspect a selected example");
        snapshot.SourceQualifiedIdentity.Should().Contain(SourceId.ToString("N"));
        snapshot.RevisionDigest.Should().HaveLength(64);
        snapshot.IsInstructionCompatible.Should().BeTrue();
        snapshot.IsAvailableForInvocation.Should().BeFalse();
        snapshot.WithUnavailableReason("unavailable").IsInstructionCompatible.Should().BeFalse();
        snapshot.RevisionDigest.Should().NotBe(Parse(Valid + "\n").RevisionDigest);
    }

    [Theory]
    [InlineData("name: Example", "invalid-required-name")]
    [InlineData("name: a--b", "invalid-required-name")]
    [InlineData("name: a-", "invalid-required-name")]
    [InlineData("name: 2example", "invalid-required-name")]
    [InlineData("name: ../escape", "invalid-required-name")]
    [InlineData("version: 1", "invalid-required-version")]
    [InlineData("version: 01.0.0", "invalid-required-version")]
    [InlineData("version: 1.0.x", "invalid-required-version")]
    [InlineData("version: 12345.0.0", "invalid-required-version")]
    [InlineData("version: 1..0", "invalid-required-version")]
    [InlineData("description: ", "unsupported-yaml-value")]
    [InlineData("description: true", null)]
    [InlineData("description: &anchor value", "unsupported-yaml-value")]
    [InlineData("description: *anchor", "unsupported-yaml-value")]
    [InlineData("description: !!python/object:os.system", "unsupported-yaml-value")]
    [InlineData("description: {command: execute}", "unsupported-yaml-value")]
    [InlineData("description: [execute]", "unsupported-yaml-value")]
    [InlineData("description: |", "unsupported-yaml-value")]
    [InlineData("description: >-", "unsupported-yaml-value")]
    [InlineData("description: ?mapping", "unsupported-yaml-value")]
    [InlineData("description: -sequence", "unsupported-yaml-value")]
    [InlineData("description: a: b", "unsupported-yaml-value")]
    [InlineData("description: text # hidden", "unsupported-yaml-value")]
    [InlineData("description: \"unterminated", "unsupported-yaml-quoting")]
    [InlineData("description: \"", "unsupported-yaml-quoting")]
    [InlineData("description: \"a\"b\"", "unsupported-yaml-quoting")]
    [InlineData("description: 'unterminated", "unsupported-yaml-quoting")]
    [InlineData("description: \"a\\nb\"", "unsupported-yaml-quoting")]
    [InlineData("description: \"a'b\"", "unsupported-yaml-quoting")]
    [InlineData("description: a\"b", "unsupported-yaml-quoting")]
    [InlineData("description: a'b", "unsupported-yaml-quoting")]
    [InlineData("description: \"quoted text\"", null)]
    [InlineData("description: 'quoted text'", null)]
    [InlineData("description: \"\"", "invalid-required-description")]
    [InlineData(" description: nested", "unsupported-yaml-structure")]
    [InlineData("Description: wrong case", "unsupported-yaml-value")]
    [InlineData("description:value", "unsupported-yaml-structure")]
    [InlineData("description: value\nname: inspect-example", "duplicate-metadata")]
    [InlineData("description: value\nentrypoint: scripts\\run.ps1", "unsupported-metadata")]
    public void Strict_flat_yaml_is_not_a_deserializer_and_required_metadata_fails_closed(string replacement, string? reason)
    {
        var key = replacement.StartsWith("name:", StringComparison.Ordinal) ? "name: inspect-example"
            : replacement.StartsWith("version:", StringComparison.Ordinal) ? "version: 1.0.0" : "description: Inspect a selected example";
        var snapshot = Parse(Valid.Replace(key, replacement, StringComparison.Ordinal));
        if (reason is null) { snapshot.IsInstructionCompatible.Should().BeTrue(); }
        else { snapshot.UnavailableReasons.Should().Contain(reason); snapshot.IsInstructionCompatible.Should().BeFalse(); }
        snapshot.Text.Should().Contain(replacement);
    }

    [Theory]
    [InlineData("[reference](https://example.invalid)", "unresolved-reference-or-active-markup")]
    [InlineData("[reference][id]", "unresolved-reference-or-active-markup")]
    [InlineData("[id]: README.md", "unresolved-reference-or-active-markup")]
    [InlineData("<script>alert('x')</script>", "unresolved-reference-or-active-markup")]
    [InlineData("https://example.invalid", "unresolved-reference-or-active-markup")]
    [InlineData("http://example.invalid", "unresolved-reference-or-active-markup")]
    [InlineData("```powershell\n& .\\scripts\\run.ps1\n```", "executable-or-unknown-code-fence")]
    [InlineData("~~~bash\nrun.sh\n~~~", "executable-or-unknown-code-fence")]
    [InlineData("Run setup.exe", "executable-workflow-reference")]
    [InlineData("Use helper.py", "executable-workflow-reference")]
    public void Hostile_markdown_is_only_exact_inert_text_and_executable_or_unresolved_behaviour_is_visible(string body, string reason)
    {
        var snapshot = Parse(Valid + "\n" + body);
        snapshot.UnavailableReasons.Should().Contain(reason);
        snapshot.Text.Should().EndWith(body);
        snapshot.IsAvailableForInvocation.Should().BeFalse();
    }

    [Theory]
    [InlineData("allowed-tools: runtime.inspect", "declared-tools-unavailable")]
    [InlineData("allowed-tools: runtime.inspect runtime.inspect", "invalid-declared-tools")]
    [InlineData("allowed-tools: Runtime.inspect", "invalid-declared-tools")]
    [InlineData("allowed-tools: ,", "invalid-declared-tools")]
    public void Declared_tool_references_are_retained_and_never_admit_runtime_dependencies(string field, string reason)
    {
        var snapshot = Parse(Valid.Replace("\n---\n#", "\n" + field + "\n---\n#", StringComparison.Ordinal));
        snapshot.UnavailableReasons.Should().Contain(reason);
        snapshot.DeclaredTools.Should().Equal(field["allowed-tools: ".Length..].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Metadata_text_encoding_byte_and_count_bounds_are_strict()
    {
        Parse("no front matter").UnavailableReasons.Should().Contain("missing-front-matter");
        Parse("---\nname: missing terminator\n").UnavailableReasons.Should().Contain("invalid-or-oversized-front-matter");
        Parse("---\n" + string.Concat(Enumerable.Repeat("name: a\n", 25)) + "---").UnavailableReasons
            .Should().Contain("invalid-or-oversized-front-matter");
        Parse(Valid.Replace("description: Inspect a selected example", "description: " + new string('x', 1050), StringComparison.Ordinal))
            .UnavailableReasons.Should().Contain("invalid-required-description");
        Parse(Valid.Replace("description: Inspect a selected example", "description: " + new string('x', 1200), StringComparison.Ordinal))
            .UnavailableReasons.Should().Contain("unsupported-yaml-structure");
        Parse(Valid.Replace("\n---\n#", "\n\n---\n#", StringComparison.Ordinal)).IsInstructionCompatible.Should().BeTrue();
        Parse(Valid + "\t").IsInstructionCompatible.Should().BeTrue();
        Parse("---\n" + string.Concat(Enumerable.Repeat("description: " + new string('é', 600) + "\n", 4)) + "---")
            .UnavailableReasons.Should().Contain("invalid-or-oversized-front-matter");
        Parse(Valid + "\0").UnavailableReasons.Should().Contain("invalid-control-text");
        Parse(Valid + "\u202e").UnavailableReasons.Should().Contain("invalid-control-text");
        Parse(Valid + "\uFEFF").UnavailableReasons.Should().Contain("invalid-control-text");
        SharedSkillSnapshot.Parse(SourceId, "a\\SKILL.md", [0xff], false).Text.Should().BeNull();
        SharedSkillSnapshot.Parse(SourceId, "a\\SKILL.md", [0xff], false).UnavailableReasons.Should().Contain("invalid-utf8");
        var large = Valid + new string('x', SharedSkillSnapshot.MaximumBytes - Encoding.UTF8.GetByteCount(Valid));
        Parse(large).Bytes.Length.Should().Be(SharedSkillSnapshot.MaximumBytes);
        ((Action)(() => Parse(large + "x"))).Should().Throw<InvalidDataException>();
        ((Action)(() => Parse(string.Empty))).Should().Throw<InvalidDataException>();
        ((Action)(() => SharedSkillSnapshot.Parse(Guid.Empty, "a", [1], false))).Should().Throw<InvalidDataException>();
        Parse(Valid, otherFiles: true).UnavailableReasons.Should().Contain("additional-package-files-not-supported");
    }

    [Fact]
    public void Source_qualified_duplicates_and_case_aliases_are_unavailable_not_hidden()
    {
        var source = new SharedSkillSource(SourceId, ".agents\\skills", new string('a', 48));
        var first = Parse(Valid);
        var second = SharedSkillSnapshot.Parse(SourceId, "other\\SKILL.md", Encoding.UTF8.GetBytes(Valid), false);
        var catalogue = new SharedSkillCatalogue(source, [first, second]);
        catalogue.Packages.Should().HaveCount(2).And.OnlyContain(package =>
            package.UnavailableReasons.Contains("duplicate-identity") && !package.IsInstructionCompatible);
        first.IsInstructionCompatible.Should().BeTrue();
        ((Action)(() => _ = new SharedSkillCatalogue(source, [first, first]))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new SharedSkillCatalogue(source, [SharedSkillSnapshot.Parse(Guid.NewGuid(), "a", [1], false)])))
            .Should().Throw<InvalidDataException>();
        new SharedSkillCatalogue(source, []).Packages.Should().BeEmpty();
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("..\\skills")]
    [InlineData(".agents\\..")]
    [InlineData(".agents\\skills.")]
    [InlineData(".agents\\skills ")]
    [InlineData(".agents\\con.txt")]
    [InlineData(".agents\\COM1")]
    [InlineData(".agents\\Lpt9")]
    [InlineData(".agents\\nul")]
    [InlineData(".agents\\aux")]
    [InlineData(".agents\\prn")]
    [InlineData("C:\\skills")]
    [InlineData(".agents/skills")]
    [InlineData(".agents\\skills:stream")]
    [InlineData(".agents\\\\skills")]
    [InlineData(".agents\\bad\0")]
    public void Profile_relative_roots_reject_profile_wide_scans_traversal_and_path_aliases(string path) =>
        ((Action)(() => _ = new SharedSkillSource(SourceId, path, new string('a', 48)))).Should().Throw<InvalidDataException>();

    [Fact]
    public void Source_registration_identity_and_deep_or_long_path_bounds_are_explicit()
    {
        foreach (var path in new[]
        {
            "", "a\\.", "a\\" + new string('b', 81), string.Join('\\', Enumerable.Repeat("a", 9)),
            string.Join('\\', Enumerable.Repeat(new string('a', 80), 4)), "a\\four", "a\\lptx", "a\\com-",
        })
        {
            var action = () => new SharedSkillSource(SourceId, path, new string('a', 48));
            if (path is "a\\four" or "a\\lptx" or "a\\com-") { action().Id.Should().Be(SourceId); }
            else { action.Should().Throw<InvalidDataException>(); }
        }
        ((Action)(() => _ = new SharedSkillSource(Guid.Empty, ".agents\\skills", new string('a', 48))))
            .Should().Throw<InvalidDataException>();
        foreach (var identity in new[] { new string('a', 47), new string('g', 48) })
        {
            ((Action)(() => _ = new SharedSkillSource(SourceId, ".agents\\skills", identity))).Should().Throw<InvalidDataException>();
        }
        ((Action)(() => SharedSkillSource.ValidateRelativePath(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SharedSkillSource(SourceId, ".agents\\skills", null!))).Should().Throw<ArgumentNullException>();
        new SharedSkillUnavailableException("source-missing").ReasonCode.Should().Be("source-missing");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a\\\\b")]
    [InlineData("a\\..\\SKILL.md")]
    [InlineData("a\\.\\SKILL.md")]
    [InlineData("a/SKILL.md")]
    [InlineData("a\\SKILL.md:stream")]
    [InlineData("a\\\0")]
    public void Exact_snapshot_and_uninspected_entry_paths_are_bounded_and_cannot_escape(string path)
    {
        var parse = () => SharedSkillSnapshot.Parse(SourceId, path, [1], false);
        parse.Should().Throw<InvalidDataException>();
        var inventory = () => SharedSkillSnapshot.Parse(SourceId, "a\\SKILL.md", [1], false, [path]);
        inventory.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Package_and_disclosure_limits_and_all_plain_code_fences_are_covered_without_execution()
    {
        foreach (var language in new[] { "", "text", "markdown", "md" })
        { Parse(Valid + "\n```" + language + "\nInert text\n```").IsInstructionCompatible.Should().BeTrue(); }
        foreach (var tools in new[] { string.Join(' ', Enumerable.Range(1, 17).Select(index => "tool-" + index)), new string('a', 81) })
        {
            Parse(Valid.Replace("\n---\n#", "\nallowed-tools: " + tools + "\n---\n#", StringComparison.Ordinal))
                .UnavailableReasons.Should().Contain("invalid-declared-tools");
        }
        var bytes = Encoding.UTF8.GetBytes(Valid);
        var emptyInventory = SharedSkillSnapshot.Parse(SourceId, "a\\SKILL.md", bytes, false, []);
        emptyInventory.UninspectedFiles.Should().BeEmpty();
        var declared = SharedSkillSnapshot.Parse(SourceId, "a\\SKILL.md", bytes, false, ["a\\script.ps1"]);
        declared.UninspectedFiles.Should().Equal("a\\script.ps1");
        declared.UnavailableReasons.Should().Contain("additional-package-files-not-supported");
        declared.WithUnavailableReason("other").UninspectedFiles.Should().Equal("a\\script.ps1");
        SharedSkillSnapshot.Parse(SourceId, "a\\SKILL.md", [0xff], true, ["a\\script.ps1"])
            .UninspectedFiles.Should().Contain("a\\script.ps1");
        SharedSkillSnapshot.Parse(SourceId, "a\\SKILL.md", [0], true, ["a\\script.ps1"])
            .UninspectedFiles.Should().Contain("a\\script.ps1");
        ((Action)(() => SharedSkillSnapshot.Parse(SourceId, new string('a', 513), bytes, false)))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => SharedSkillSnapshot.Parse(SourceId, "a", bytes, false, [new string('a', 513)])))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => SharedSkillSnapshot.Parse(SourceId, "a", bytes, false, Enumerable.Repeat("a", 257).ToArray())))
            .Should().Throw<InvalidDataException>();
        var source = new SharedSkillSource(SourceId, ".agents\\skills", new string('a', 48));
        var maximum = Enumerable.Range(0, 32).Select(index =>
            SharedSkillSnapshot.Parse(SourceId, $"a-{index}\\SKILL.md", bytes, false)).ToArray();
        new SharedSkillCatalogue(source, maximum).Packages.Should().HaveCount(32);
        ((Action)(() => _ = new SharedSkillCatalogue(source, maximum.Append(Parse(Valid)).ToArray())))
            .Should().Throw<InvalidDataException>();
        var oversized = Enumerable.Range(0, 17).Select(index => SharedSkillSnapshot.Parse(SourceId, $"a-{index}", new byte[65536], false)).ToArray();
        ((Action)(() => _ = new SharedSkillCatalogue(source, oversized))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new SharedSkillCatalogue(source, [null!]))).Should().Throw<InvalidDataException>();
        var invalid = new SharedSkillCatalogue(source, [SharedSkillSnapshot.Parse(SourceId, "a", [0xff], false)]);
        invalid.Packages.Should().ContainSingle();
        invalid.Source.Should().Be(source);
    }

    private static SharedSkillSnapshot Parse(string text, bool otherFiles = false) =>
        SharedSkillSnapshot.Parse(SourceId, "example\\SKILL.md", Encoding.UTF8.GetBytes(text), otherFiles);
}
