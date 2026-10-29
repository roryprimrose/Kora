using System.Text;

using AwesomeAssertions;

using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Core.UnitTests.Customizations;

public sealed class CustomizationContractTests
{
    [Theory]
    [InlineData(CustomizationKind.Skill)]
    [InlineData(CustomizationKind.Instruction)]
    [InlineData(CustomizationKind.Agent)]
    [InlineData(CustomizationKind.McpServer)]
    [InlineData(CustomizationKind.Prompt)]
    public void Each_kind_has_an_exact_primary_and_independent_immutable_bytes(CustomizationKind kind)
    {
        var name = CustomizationLayout.PrimaryFile(kind, "example");
        var bytes = ArtifactFileDocument.Encode(CustomizationLayout.Template(kind, "example"), true);
        var file = new CustomizationFile(name, bytes);
        bytes[0] = 0;
        file.HasBom.Should().BeTrue();
        file.Text.Should().NotContain("\0");
        var snapshot = new CustomizationSnapshot(new("test", kind, CustomizationScope.Kora, "example", "source", name),
            "revision", [file]);
        file.CopyBytes()[0].Should().Be(0xef);
        file.WithText("Changed").HasBom.Should().BeTrue();
        file.WithText("Changed").Digest.Should().NotBe(file.Digest);
        snapshot.Files.Should().ContainSingle();
        if (kind == CustomizationKind.McpServer)
        { ((Action)(() => CustomizationLayout.ArtifactKind(kind))).Should().Throw<InvalidDataException>(); }
        else { Enum.IsDefined(CustomizationLayout.ArtifactKind(kind)).Should().BeTrue(); }
    }

    [Theory]
    [InlineData(@"..\outside.md")]
    [InlineData(@"C:\outside.md")]
    [InlineData("outside/file.md")]
    [InlineData("CON.txt")]
    [InlineData("com1.md")]
    [InlineData("PRN.md")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("LPT9.txt")]
    [InlineData("COM0.txt")]
    [InlineData(".")]
    [InlineData("bad\tfile")]
    [InlineData("bad?file")]
    [InlineData(@"a\\b")]
    [InlineData("a.")]
    [InlineData("a ")]
    [InlineData(@"a\b\c\d\e.md")]
    [InlineData("")]
    public void Component_names_cannot_escape_or_alias_the_selected_package(string name) =>
        ((Action)(() => _ = new CustomizationFile(name, []))).Should().Throw<InvalidDataException>();

    [Fact]
    public void Invalid_encoding_binary_oversize_duplicate_and_missing_primary_are_not_omitted()
    {
        ((Action)(() => _ = new CustomizationFile("a.md", [0xff]))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new CustomizationFile("a.md", [0]))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new CustomizationFile("a.md", new byte[65537]))).Should().Throw<InvalidDataException>();
        var source = new CustomizationSource("test", CustomizationKind.Skill, CustomizationScope.Kora, "test", "source", "SKILL.md");
        ((Action)(() => _ = new CustomizationSnapshot(source, "revision", []))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new CustomizationSnapshot(source, "revision", [new("SKILL.md", []), new("skill.md", [])])))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new CustomizationSnapshot(source, "revision", [new("other.md", [])]))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Complete_file_and_path_bounds_are_enforced_without_silently_dropping_components()
    {
        var source = new CustomizationSource("test", CustomizationKind.Skill, CustomizationScope.Kora, "test", "source", "SKILL.md");
        var primary = new CustomizationFile("SKILL.md", []);
        Action invalidName = () => CustomizationFile.ValidateName(null!);
        invalidName.Should().Throw<ArgumentNullException>();
        foreach (var name in new[] { new string('a', 81), string.Join('\\', Enumerable.Repeat(new string('a', 60), 4)) })
        { ((Action)(() => CustomizationFile.ValidateName(name))).Should().Throw<InvalidDataException>(); }
        foreach (var valid in new[] { "COMx.txt", "COM10.txt", "LPT.txt", "COM", "LPTz.txt", "LPT0a.txt", "COM-.txt" })
        { CustomizationFile.ValidateName(valid).Should().Be(valid); }
        CustomizationFile[] tooMany = [primary, .. Enumerable.Range(0, 32).Select(index => new CustomizationFile(index + ".md", []))];
        ((Action)(() => CustomizationSnapshot.ValidateFiles(source, tooMany))).Should().Throw<InvalidDataException>();
        CustomizationFile[] tooLarge = [primary, .. Enumerable.Range(0, 17).Select(index =>
            new CustomizationFile(index + ".md", Encoding.UTF8.GetBytes(new string('x', 65536))))];
        ((Action)(() => CustomizationSnapshot.ValidateFiles(source, tooLarge))).Should().Throw<InvalidDataException>();
        ((Action)(() => CustomizationSnapshot.ValidateFiles(source, [primary, null!]))).Should().Throw<InvalidDataException>();
        ((Action)(() => CustomizationSnapshot.ValidateFiles(source with { Kind = CustomizationKind.Agent },
            [primary, new("companion.md", [])]))).Should().Throw<InvalidDataException>();
        ((Action)(() => CustomizationLayout.PrimaryFile((CustomizationKind)999, "x"))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(ArtifactKind.Skill)]
    [InlineData(ArtifactKind.Agent)]
    [InlineData(ArtifactKind.Instruction)]
    [InlineData(ArtifactKind.Prompt)]
    public void Disabled_candidates_remain_inspectable_but_are_never_runtime_options_or_captured_authority(ArtifactKind kind)
    {
        var policy = new Policy();
        var definition = new ArtifactDefinition("test", kind, "Example", "Description", "example", ["example"],
            "bundled", "1", new string('a', 64), "Guidance") { PreservesCapturedUse = true };
        using var catalogue = new ArtifactCatalogue([definition], policy);
        var changes = 0;
        catalogue.Changed += (_, _) => changes++;
        catalogue.Artifacts.Should().ContainSingle();
        policy.Set(true);
        catalogue.Candidates.Should().ContainSingle().Which.IsUseDisabled.Should().BeTrue();
        catalogue.Artifacts.Should().BeEmpty();
        catalogue.IsCurrent("test", definition.Digest).Should().BeFalse();
        catalogue.IsCapturedRevisionCurrent("test", definition.Digest).Should().BeFalse();
        catalogue.Publish([definition]);
        catalogue.Artifacts.Should().BeEmpty("reload cannot discard the persisted exclusion");
        policy.Set(false);
        catalogue.Artifacts.Should().ContainSingle();
        changes.Should().Be(2);
        catalogue.UseControlRevision.Should().Be(2);
    }

    [Fact]
    public void Policy_masked_candidates_remain_one_snapshot_until_source_or_use_control_changes()
    {
        var policy = new Policy();
        var definition = new ArtifactDefinition("test", ArtifactKind.Skill, "Example", "Description", "example", ["example"],
            "bundled", "1", new string('a', 64), "Guidance");
        using var catalogue = new ArtifactCatalogue([definition], policy);
        var original = catalogue.Candidates;
        catalogue.Candidates.Should().BeSameAs(original);
        policy.Set(true);
        var disabled = catalogue.Candidates;
        disabled.Should().NotBeSameAs(original);
        catalogue.Candidates.Should().BeSameAs(disabled);
        disabled.Single().IsUseDisabled.Should().BeTrue();
        policy.Set(false);
        var enabled = catalogue.Candidates;
        enabled.Should().NotBeSameAs(disabled).And.NotBeSameAs(original);
        catalogue.Candidates.Should().BeSameAs(enabled);
        catalogue.Publish([new ArtifactDefinition("test", ArtifactKind.Skill, "Example", "Description", "example", ["example"],
            "bundled", "2", new string('b', 64), "Updated")]);
        catalogue.Candidates.Should().NotBeSameAs(enabled);
        catalogue.Candidates.Single().Content.Should().Be("Updated");
        var updated = catalogue.Candidates;
        policy.Set(true, notify: false);
        catalogue.Candidates.Should().NotBeSameAs(updated);
        catalogue.Candidates.Single().IsUseDisabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reload_failure_withholds_file_use_before_notifications_and_leaves_failure_as_the_terminal_notice(bool withPolicy)
    {
        var file = new ArtifactDefinition("file.test", ArtifactKind.Instruction, "File", "Description", "file", ["file"],
            "user-profile", "1", new string('a', 64), "Guidance") { UseControlId = "file.source", PreservesCapturedUse = true };
        var bundled = new ArtifactDefinition("bundled.test", ArtifactKind.Skill, "Bundled", "Description", "bundled", ["bundled"],
            "bundled", "1", new string('b', 64), "Guidance");
        using var catalogue = new ArtifactCatalogue([file, bundled], withPolicy ? new Policy() : null);
        var original = catalogue.Candidates;
        var notifications = new List<string>();
        IReadOnlyList<ArtifactDefinition>? withheld = null;
        var failure = new IOException("Synthetic reload failure");
        void RequireWithheld()
        {
            catalogue.IsReloadUnavailable.Should().BeTrue();
            catalogue.ReloadFailureRevision.Should().Be(1);
            catalogue.SourceCandidates.Should().Equal(file, bundled);
            catalogue.Candidates.Should().NotBeSameAs(original);
            catalogue.Candidates.Single(item => string.Equals(item.Id, file.Id, StringComparison.Ordinal)).IsUseEnabled.Should().BeFalse();
            catalogue.IsCurrent(file.Id, file.Digest).Should().BeFalse();
            catalogue.IsCapturedRevisionCurrent(file.Id, file.Digest).Should().BeFalse();
            catalogue.Artifacts.Should().ContainSingle().Which.Should().BeSameAs(bundled);
        }
        void ReloadFailed(object? sender, ArtifactCatalogueReloadFailedEventArgs args)
        {
            RequireWithheld();
            args.Exception.Should().BeSameAs(failure);
            catalogue.Candidates.Should().BeSameAs(withheld);
            notifications.Add("reload-failed");
        }
        catalogue.ReloadFailed += ReloadFailed;
        catalogue.Changed += (_, _) =>
        {
            RequireWithheld();
            withheld = catalogue.Candidates;
            notifications.Add("changed");
            catalogue.ReloadFailed -= ReloadFailed;
        };

        catalogue.ReportReloadFailure(failure);

        notifications.Should().Equal(["changed", "reload-failed"],
            "the failure-specific recovery notice must not be overwritten by generic catalogue invalidation");
    }

    [Theory]
    [InlineData("references", @"references\notes.md")]
    [InlineData("REFERENCES", @"references\notes.md")]
    [InlineData(@"a\b", @"a\b\notes.md")]
    public void A_component_cannot_also_be_another_components_directory(string file, string child)
    {
        var source = new CustomizationSource("test", CustomizationKind.Skill, CustomizationScope.Kora, "test", "source", "SKILL.md");
        Action validate = () => CustomizationSnapshot.ValidateFiles(source, [new("SKILL.md", []), new(file, []), new(child, [])]);
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Mcp_replacement_and_addition_preserve_unrelated_bytes_comments_and_root_properties()
    {
        const string text = "{\r\n// profile comment\r\n\"inputs\": [], \"servers\": {\"a\": {\"url\":\"https://a.invalid\"}, /*keep*/ \"b\": {\"command\":\"tool\"},}, \"other\": 42\r\n}";
        var document = new McpDefinitionDocument(Encoding.UTF8.GetBytes(text));
        document.Names.Should().Equal("a", "b");
        Encoding.UTF8.GetString(document.Read("b")).Should().Be("{\"command\":\"tool\"}");
        var replaced = Encoding.UTF8.GetString(document.Replace("a", Encoding.UTF8.GetBytes("{\"url\":\"https://changed.invalid\"}")));
        replaced.Should().Be(text.Replace("{\"url\":\"https://a.invalid\"}", "{\"url\":\"https://changed.invalid\"}", StringComparison.Ordinal));
        var added = document.Replace("new", Encoding.UTF8.GetBytes("{\"command\":\"new-tool\"}"));
        var parsed = new McpDefinitionDocument(added);
        parsed.Names.Should().Equal("a", "b", "new");
        parsed.Read("b").Should().Equal(document.Read("b"));
        Encoding.UTF8.GetString(added).Should().Contain("/*keep*/").And.Contain("\"other\": 42");
        new McpDefinitionDocument(Encoding.UTF8.GetBytes("{\"mcpServers\":{}}")).Replace("a",
            Encoding.UTF8.GetBytes("{\"command\":\"tool\"}")).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"servers\":{},\"mcpServers\":{}}")]
    [InlineData("{\"servers\":[]}")]
    [InlineData("{\"servers\":{\"x\":false}}")]
    [InlineData("{\"servers\":{\"x\":{},\"x\":{}}}")]
    [InlineData("{\"servers\":")]
    public void Malformed_or_ambiguous_mcp_configuration_is_not_reinterpreted(string text) =>
        ((Action)(() => _ = new McpDefinitionDocument(Encoding.UTF8.GetBytes(text)))).Should().Throw<InvalidDataException>();

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"command\":\"\"}")]
    [InlineData("{\"command\":false}")]
    [InlineData("{\"url\":false}")]
    [InlineData("{\"url\":\"not a URL\"}")]
    [InlineData("false")]
    [InlineData("{")]
    [InlineData("{\"url\":\"file:///C:/private\"}")]
    [InlineData("{\"command\":\"x\",\"url\":\"https://a.invalid\"}")]
    [InlineData("{\"url\":\"https://a.invalid\",\"url\":\"https://b.invalid\"}")]
    public void Invalid_mcp_server_proposals_are_rejected(string text) =>
        ((Action)(() => McpDefinitionDocument.ValidateServer(Encoding.UTF8.GetBytes(text)))).Should().Throw<InvalidDataException>();

    [Fact]
    public void Mcp_bounds_names_bom_missing_entries_and_nested_values_remain_explicit()
    {
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[256 * 1024 + 1] })
        { ((Action)(() => _ = new McpDefinitionDocument(bytes))).Should().Throw<InvalidDataException>(); }
        var text = "{\"servers\":{\"a\":{\"command\":\"tool\",\"args\":[{\"value\":true},null]}}}";
        var document = new McpDefinitionDocument(ArtifactFileDocument.Encode(text, true));
        document.Read("a").Should().NotBeEmpty();
        var added = document.Replace("b", Encoding.UTF8.GetBytes("{\"url\":\"http://localhost/mcp\"}"));
        added.Take(3).Should().Equal(Encoding.UTF8.Preamble.ToArray());
        ((Action)(() => document.Read("missing"))).Should().Throw<InvalidDataException>();
        foreach (var name in new[] { "", " ", new string('a', 129), "control\n" })
        { ((Action)(() => document.Replace(name, "{}"u8))).Should().Throw<InvalidDataException>(); }
        var many = "{\"servers\":{" + string.Join(',', Enumerable.Range(0, 129).Select(index => "\"" + index + "\":{}")) + "}}";
        ((Action)(() => _ = new McpDefinitionDocument(Encoding.UTF8.GetBytes(many)))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Use_identities_are_stable_source_qualified_and_policy_cannot_be_bypassed_by_reload_or_alias()
    {
        var path = Path.Combine(Path.GetTempPath(), "kora-source", "definition.md");
        ArtifactUseIdentity.File(path).Should().Be(ArtifactUseIdentity.File(path.ToUpperInvariant()));
        ArtifactUseIdentity.McpServerId(path, "a").Should().NotBe(ArtifactUseIdentity.McpServerId(path, "A"));
        ArtifactUseIdentity.Mcp("server").Should().Be("mcp.server");
        ((Action)(() => ArtifactUseIdentity.File("relative.md"))).Should().Throw<ArgumentException>();
        ((Action)(() => ArtifactUseIdentity.File(" "))).Should().Throw<ArgumentException>();
        ((Action)(() => ArtifactUseIdentity.Mcp(" "))).Should().Throw<ArgumentException>();
        var policy = new Policy { Identity = "test" };
        var definition = new ArtifactDefinition("test", ArtifactKind.Skill, "Example", "Description", "example", ["example"],
            "bundled", "1", new string('a', 64), "Guidance") { UseControlId = "file.source" };
        using var catalogue = new ArtifactCatalogue([definition], policy);
        catalogue.IsCapturedRevisionCurrent("other", definition.Digest).Should().BeFalse();
        catalogue.IsCapturedRevisionCurrent("test", "other").Should().BeFalse();
        catalogue.IsCapturedRevisionCurrent("test", definition.Digest).Should().BeTrue();
        policy.Set(true);
        catalogue.Artifacts.Should().BeEmpty("an artifact-ID hold also masks a mapped physical source");
        using var noPolicy = new ArtifactCatalogue([definition]);
        noPolicy.UseControlRevision.Should().Be(0);
        var changes = 0;
        catalogue.Changed += (_, _) => changes++;
        catalogue.Dispose();
        policy.Set(false);
        changes.Should().Be(0);
    }

    private sealed class Policy : IArtifactUsePolicy
    {
        private bool disabled;
        public event EventHandler? Changed;
        public long Revision { get; private set; }
        internal string? Identity { get; init; }
        public bool IsDisabled(string identity) => disabled && (Identity is null || string.Equals(Identity, identity, StringComparison.Ordinal));
        public bool IsRevisionAllowed(string identity, string digest) => !IsDisabled(identity);
        internal void Set(bool value, bool notify = true)
        {
            disabled = value; Revision++;
            if (notify) { Changed?.Invoke(this, EventArgs.Empty); }
        }
    }
}
