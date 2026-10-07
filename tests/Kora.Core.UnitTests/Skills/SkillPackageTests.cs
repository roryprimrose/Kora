using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Skills;

namespace Kora.Core.UnitTests.Skills;

public sealed class SkillPackageTests
{
    private const string ManifestId = "Kora.Test.Manifest";
    private const string ManifestName = "skills\\test\\manifest.json";
    private const string ManifestText = """
        {
          "schemaVersion": 1, "id": "kora.test", "version": "1.0.0",
          "name": "Test", "description": "Fixed action", "action": "test.action",
          "entryPoint": "scripts\\entry.ps1", "parameterContract": "test.v1",
          "runtime": "powershell7", "dependencies": ["kora.test.adapter"],
          "helperLoadOrder": ["scripts\\helper.ps1"],
          "files": [
            {"name":"skills\\test\\skill.md","resourceId":"Kora.Test.Instructions","kind":"instructions"},
            {"name":"skills\\test\\fixture.json","resourceId":"Kora.Test.Fixture","kind":"fixture"},
            {"name":"scripts\\entry.ps1","resourceId":"Kora.Test.Entry","kind":"entry"},
            {"name":"scripts\\helper.ps1","resourceId":"Kora.Test.Helper","kind":"helper"}
          ]
        }
        """;

    [Fact]
    public void Independent_canonical_vectors_and_full_name_ordinal_order_are_stable()
    {
        SkillResourceSnapshot a = new("scripts\\a.ps1", "Kora.A", [0x61, 0x62]);
        SkillResourceSnapshot b = new("scripts\\b.ps1", "Kora.B", [0x63]);
        SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [b, a])
            .Should().Be("57e8d8ad0c7a993252c17a068caecc526f06e48d8ae9be7f24ce974f334ff9b7");
        SkillPackageDigest.Compute(SkillPackageDigest.DefinitionVersion, [a, b])
            .Should().Be("8bb1937c567050675d955ab0db78c3d706d7c2d238f30d7007b23d2d54df57b2");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            SkillResourceSnapshot first = new("scripts\\a\\z.ps1", "Kora.First", [1]);
            SkillResourceSnapshot second = new("scripts\\b\\a.ps1", "Kora.Second", [2]);
            SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [second, first])
                .Should().Be("866c01bb9c2d7919533f830a44a0ca41d1d88c2c26f71cc29a5f55d37b0f831f");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Original_bytes_BOM_CRLF_and_snapshots_are_not_reencoded_or_mutable()
    {
        byte[] bytes = [0xef, 0xbb, 0xbf, 0x61, 13, 10];
        var file = new SkillResourceSnapshot("scripts\\a.ps1", "Kora.A", bytes);
        bytes[3] = 0x62;
        var copy = file.Bytes.ToArray();
        copy[3] = 0x63;
        file.Bytes.ToArray().Should().Equal(0xef, 0xbb, 0xbf, 0x61, 13, 10);
        file.Text.Should().Be("\uFEFFa\r\n");
        file.Digest.Should().HaveLength(64);
        var hash = SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [file]);
        foreach (var content in new[] { "a\r\n", "\uFEFFa\n", "\uFEFFb\r\n" })
        {
            SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [Resource(file.Name, file.ResourceId, content)])
                .Should().NotBe(hash);
        }
    }

    [Theory]
    [InlineData("Scripts\\a.ps1")]
    [InlineData("scripts/a.ps1")]
    [InlineData("scripts\\..\\a.ps1")]
    [InlineData("scripts\\.\\a.ps1")]
    [InlineData("\\scripts\\a.ps1")]
    [InlineData("scripts\\\\a.ps1")]
    [InlineData("c:\\a.ps1")]
    [InlineData("scripts\\a:stream")]
    [InlineData("scripts\\\u00e9.ps1")]
    public void Logical_aliases_are_rejected(string name) =>
        ((Action)(() => Resource(name, "Kora.A", "x"))).Should().Throw<InvalidDataException>();

    [Fact]
    public void Names_resource_ids_encoding_and_exact_byte_limits_are_enforced()
    {
        ((Action)(() => Resource("", "Kora.A", "x"))).Should().Throw<ArgumentException>();
        ((Action)(() => Resource(new string('a', 257), "Kora.A", "x"))).Should().Throw<InvalidDataException>();
        ((Action)(() => Resource("a", "", "x"))).Should().Throw<ArgumentException>();
        ((Action)(() => Resource("a", "Kora/A", "x"))).Should().Throw<InvalidDataException>();
        ((Action)(() => Resource("a", new string('a', 129), "x"))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new SkillResourceSnapshot("a", "Kora.A", []))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new SkillResourceSnapshot("a", "Kora.A", [0xff]))).Should().Throw<InvalidDataException>();
        ((Action)(() => Resource("a", "Kora.A", "a\0"))).Should().Throw<InvalidDataException>();
        ((Action)(() => Resource("a", "Kora.A", new string('x', SkillResourceSnapshot.MaximumBytes + 1))))
            .Should().Throw<InvalidDataException>();
        Resource("scripts\\abc_01-x.ps1", "Kora.A01", new string('x', SkillResourceSnapshot.MaximumBytes))
            .Bytes.Length.Should().Be(SkillResourceSnapshot.MaximumBytes);
        Resource("a", "Kora.A", new string('\u00e9', SkillResourceSnapshot.MaximumBytes / 2))
            .Bytes.Length.Should().Be(SkillResourceSnapshot.MaximumBytes);
        ((Action)(() => Resource("a", "Kora.A", new string('\u00e9', SkillResourceSnapshot.MaximumBytes / 2 + 1))))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Digest_rejects_empty_oversize_duplicate_and_aliased_sets_and_unknown_versions()
    {
        var a = Resource("a", "Kora.A", "ab");
        var b = Resource("b", "Kora.B", "c");
        ((Action)(() => SkillPackageDigest.Compute("other", [a]))).Should().Throw<InvalidDataException>();
        ((Action)(() => SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, null!))).Should().Throw<ArgumentNullException>();
        foreach (var set in new IReadOnlyList<SkillResourceSnapshot>[]
        {
            [], [null!], [a, a], [a, Resource("b", "kora.a", "c")],
            Enumerable.Range(0, 17).Select(i => Resource("a" + i.ToString(CultureInfo.InvariantCulture),
                "Kora.A" + i.ToString(CultureInfo.InvariantCulture), "x")).ToArray(),
            Enumerable.Range(0, 5).Select(i => Resource("a" + i.ToString(CultureInfo.InvariantCulture),
                "Kora.A" + i.ToString(CultureInfo.InvariantCulture), new string('x', SkillResourceSnapshot.MaximumBytes))).ToArray(),
        })
        {
            ((Action)(() => SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, set))).Should().Throw<InvalidDataException>();
        }
        SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [a, b]).Should().NotBe(
            SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion,
                [Resource("a", "Kora.A", "a"), Resource("b", "Kora.B", "bc")]));
        SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [a]).Should().NotBe(
            SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [Resource("renamed", "Kora.A", "ab")]));
        SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [a]).Should().NotBe(
            SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion, [a, b]));
        SkillPackageDigest.Compute(SkillPackageDigest.ScriptSetVersion,
            Enumerable.Range(0, 16).Select(i => Resource("a" + i.ToString(CultureInfo.InvariantCulture),
                "Kora.A" + i.ToString(CultureInfo.InvariantCulture), new string('x', 16384))).ToArray()).Should().HaveLength(64);
    }

    [Theory]
    [InlineData("schemaVersion", "2")]
    [InlineData("schemaVersion", "\"1\"")]
    [InlineData("schemaVersion", "1.5")]
    [InlineData("id", "\"Upper\"")]
    [InlineData("version", "\"BAD\"")]
    [InlineData("name", "null")]
    [InlineData("name", "42")]
    [InlineData("description", "\" \"")]
    [InlineData("action", "\"bad/action\"")]
    [InlineData("entryPoint", "\"scripts\\\\missing.ps1\"")]
    [InlineData("parameterContract", "\"bad:value\"")]
    [InlineData("runtime", "\"BAD\"")]
    [InlineData("dependencies", "[]")]
    [InlineData("dependencies", "{}")]
    [InlineData("dependencies", "[\"a\",\"a\"]")]
    [InlineData("dependencies", "[null]")]
    [InlineData("helperLoadOrder", "[]")]
    [InlineData("helperLoadOrder", "[\"scripts\\\\helper.ps1\",\"scripts\\\\helper.ps1\"]")]
    [InlineData("files", "[]")]
    [InlineData("files", "{}")]
    [InlineData("files", "[{}]")]
    public void Strict_manifest_fields_domain_values_and_orders_fail_closed(string field, string json)
    {
        var root = JsonNode.Parse(ManifestText)!.AsObject();
        root[field] = JsonNode.Parse(json);
        ((Action)(() => SkillPackageManifest.Parse(Resource(ManifestName, ManifestId, root.ToJsonString()))))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Manifest_rejects_unknown_duplicate_missing_deep_and_invalid_JSON_and_bounds()
    {
        foreach (var text in new[]
        {
            "[]", "{", ManifestText.Replace("\"schemaVersion\": 1,", "", StringComparison.Ordinal),
            ManifestText.Replace("\"schemaVersion\": 1,", "\"schemaVersion\":1,\"schemaVersion\":1,", StringComparison.Ordinal),
            ManifestText.Replace("\"schemaVersion\": 1,", "\"schemaVersion\":1,\"unknown\":1,", StringComparison.Ordinal),
            new string('[', 9) + "1" + new string(']', 9), "\uFEFF\uFEFF" + ManifestText,
        })
        {
            ((Action)(() => SkillPackageManifest.Parse(Resource(ManifestName, ManifestId, text)))).Should().Throw<InvalidDataException>();
        }
        ((Action)(() => SkillPackageManifest.Parse(null!))).Should().Throw<ArgumentNullException>();
        foreach (var field in new[] { "name", "description", "id", "dependencies", "helperLoadOrder", "files" })
        {
            var root = JsonNode.Parse(ManifestText)!.AsObject();
            root[field] = field is "name" or "description" or "id"
                ? JsonValue.Create(new string('a', 513))
                : new JsonArray(Enumerable.Range(0, 17).Select(_ => JsonValue.Create("a")).ToArray<JsonNode?>());
            ((Action)(() => SkillPackageManifest.Parse(Resource(ManifestName, ManifestId, root.ToJsonString()))))
                .Should().Throw<InvalidDataException>();
        }
        foreach (var change in new[] { "kind", "fixture-kind", "extension", "entry", "instructions", "extra-entry", "different-entry" })
        {
            var root = JsonNode.Parse(ManifestText)!.AsObject();
            var files = root["files"]!.AsArray();
            if (change is "kind") { files[0]!["kind"] = "unknown"; }
            if (change is "fixture-kind") { files[1]!["kind"] = "unknown"; }
            if (change is "extension") { files[0]!["name"] = "a.ps1"; }
            if (change is "entry") { files[2]!["kind"] = "helper"; }
            if (change is "instructions") { files[0]!["kind"] = "fixture"; files[0]!["name"] = "a.json"; }
            if (change is "extra-entry") { files.Add(files[2]!.DeepClone()); }
            if (change is "different-entry")
            {
                var entry = files[2]!.DeepClone();
                entry["name"] = "scripts\\other.ps1";
                entry["resourceId"] = "Kora.Other.Entry";
                files.Add(entry);
            }
            ((Action)(() => SkillPackageManifest.Parse(Resource(ManifestName, ManifestId, root.ToJsonString()))))
                .Should().Throw<InvalidDataException>();
        }
        SkillPackageManifest.Parse(Resource(ManifestName, ManifestId, "\uFEFF" + ManifestText)).Id.Should().Be("kora.test");
    }

    [Fact]
    public void Catalogue_is_immutable_shared_identity_and_content_comparison_is_not_authority()
    {
        var resources = Resources();
        var catalogue = new SkillPackageCatalogue([ManifestId], resources);
        var package = catalogue.Packages.Single();
        resources.Clear();
        package.Files.Should().HaveCount(5);
        package.Manifest.Id.Should().Be("kora.test");
        package.Manifest.Version.Should().Be("1.0.0");
        package.Manifest.Name.Should().Be("Test");
        package.Manifest.Description.Should().Be("Fixed action");
        package.Manifest.Action.Should().Be("test.action");
        package.Manifest.EntryPoint.Should().Be("scripts\\entry.ps1");
        package.Manifest.ParameterContract.Should().Be("test.v1");
        package.Manifest.Runtime.Should().Be("powershell7");
        package.Manifest.Dependencies.Should().Equal("kora.test.adapter");
        package.Manifest.HelperLoadOrder.Should().Equal("scripts\\helper.ps1");
        package.IsAvailableForInvocation.Should().BeFalse();
        package.Manifest.IsActionAvailableForInvocation.Should().BeFalse();
        catalogue.Dependents("Kora.Test.Helper").Should().Equal(package);
        catalogue.Dependents("missing").Should().BeEmpty();
        package.HasSameDeclaredContent(Binding(package)).Should().BeTrue();
        ((Action)(() => package.HasSameDeclaredContent(null!))).Should().Throw<ArgumentNullException>();
        foreach (var field in new[] { "source", "skill", "action", "definition", "declared" })
        {
            package.HasSameDeclaredContent(Binding(package, field)).Should().BeFalse();
        }
        SkillPackageSnapshot.UnavailableReason.Should().Contain("No interpreter");
        SkillPackageSnapshot.TransitiveDisclosure.Should().Contain("best effort").And.Contain("responsibility").And.Contain("grants nothing");
        SkillPackageSnapshot.TransitiveGaps.Should().Contain("unresolved");
    }

    [Fact]
    public void Catalogue_rejects_missing_extra_reordered_mapping_alias_collision_and_resource_bounds()
    {
        ((Action)(() => _ = new SkillPackageCatalogue(null!, Resources()))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SkillPackageCatalogue([ManifestId], null!))).Should().Throw<ArgumentNullException>();
        foreach (var ids in new IReadOnlyList<string>[] { [], [ManifestId, ManifestId], [ManifestId.ToLowerInvariant()], ["missing"],
            Enumerable.Repeat(ManifestId, 9).ToArray() })
        {
            ((Action)(() => _ = new SkillPackageCatalogue(ids, Resources()))).Should().Throw<InvalidDataException>();
        }
        foreach (var scenario in new[] { "empty", "null", "count", "duplicate-id", "duplicate-name", "missing",
            "name-map", "extra", "not-json", "resource-alias", "manifest-script-alias" })
        {
            var resources = Resources();
            if (scenario is "empty") { resources.Clear(); }
            if (scenario is "null") { resources.Add(null!); }
            if (scenario is "count") { resources.AddRange(Enumerable.Repeat(resources[0], 33)); }
            if (scenario is "duplicate-id") { resources.Add(Resource("other", ManifestId, "x")); }
            if (scenario is "duplicate-name") { resources.Add(Resource(ManifestName, "Kora.Other", "x")); }
            if (scenario is "missing") { resources.RemoveAt(4); }
            if (scenario is "name-map") { resources[4] = Resource("scripts\\other.ps1", "Kora.Test.Helper", "x"); }
            if (scenario is "extra") { resources.Add(Resource("extra", "Kora.Extra", "x")); }
            if (scenario is "not-json") { resources[0] = Resource("manifest.md", ManifestId, ManifestText); }
            if (scenario is "resource-alias") { resources[4] = Resource("scripts\\helper.ps1", "kora.test.helper", "x"); }
            if (scenario is "manifest-script-alias")
            {
                resources[0] = Resource(ManifestName, ManifestId, ManifestText.Replace("Kora.Test.Helper", "Kora.Test.Entry", StringComparison.Ordinal));
            }
            ((Action)(() => _ = new SkillPackageCatalogue([ManifestId], resources))).Should().Throw<InvalidDataException>();
        }
        foreach (var duplicateAction in new[] { false, true })
        {
            var resources = Resources();
            resources.Add(Resource("skills\\second\\manifest.json", "Kora.Second.Manifest", duplicateAction
                ? ManifestText.Replace("kora.test\"", "kora.second\"", StringComparison.Ordinal) : ManifestText));
            ((Action)(() => _ = new SkillPackageCatalogue([ManifestId, "Kora.Second.Manifest"], resources)))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void Every_approval_relevant_change_invalidates_exact_content_without_fabricating_runtime_identity()
    {
        var original = new SkillPackageCatalogue([ManifestId], Resources()).Packages[0];
        foreach (var index in new[] { 0, 1, 2, 3, 4 })
        {
            var changed = Resources();
            var file = changed[index];
            changed[index] = Resource(file.Name, file.ResourceId, file.Text + " ");
            var package = new SkillPackageCatalogue([ManifestId], changed).Packages[0];
            package.DeclaredResourceDigest.Should().NotBe(original.DeclaredResourceDigest);
            package.HasSameDeclaredContent(Binding(original)).Should().BeFalse();
            Binding(original).HasObservedContentChange(Binding(package)).Should().BeTrue();
            if (index < 3)
            {
                package.DefinitionDigest.Should().NotBe(original.DefinitionDigest);
                package.ScriptSetDigest.Should().Be(original.ScriptSetDigest);
            }
            else
            {
                package.ScriptSetDigest.Should().NotBe(original.ScriptSetDigest);
                package.DefinitionDigest.Should().Be(original.DefinitionDigest);
            }
        }
        var resources = Resources();
        var root = JsonNode.Parse(ManifestText)!.AsObject();
        var array = root["files"]!.AsArray();
        var last = array[3]!.DeepClone();
        array.RemoveAt(3);
        array.Insert(0, last);
        resources[0] = Resource(ManifestName, ManifestId, root.ToJsonString());
        var reordered = new SkillPackageCatalogue([ManifestId], resources).Packages[0];
        reordered.ScriptSetDigest.Should().Be(original.ScriptSetDigest);
        reordered.DefinitionDigest.Should().NotBe(original.DefinitionDigest);
        resources = Resources();
        resources[4] = Resource("scripts\\helper.ps1", "Kora.Changed.Helper", "helper");
        resources[0] = Resource(ManifestName, ManifestId,
            ManifestText.Replace("Kora.Test.Helper", "Kora.Changed.Helper", StringComparison.Ordinal));
        var remapped = new SkillPackageCatalogue([ManifestId], resources).Packages[0];
        remapped.ScriptSetDigest.Should().Be(original.ScriptSetDigest);
        remapped.DefinitionDigest.Should().NotBe(original.DefinitionDigest);
        remapped.DeclaredResourceDigest.Should().NotBe(original.DeclaredResourceDigest);
        resources = Resources();
        resources[0] = Resource(ManifestName, "Kora.Remapped.Manifest", ManifestText);
        var remappedManifest = new SkillPackageCatalogue(["Kora.Remapped.Manifest"], resources).Packages[0];
        remappedManifest.DefinitionDigest.Should().Be(original.DefinitionDigest);
        remappedManifest.ScriptSetDigest.Should().Be(original.ScriptSetDigest);
        remappedManifest.DeclaredResourceDigest.Should().NotBe(original.DeclaredResourceDigest);
    }

    [Fact]
    public void Helper_initialization_order_is_definition_bound_but_never_the_hash_order()
    {
        var resources = Resources();
        var root = JsonNode.Parse(ManifestText)!.AsObject();
        root["files"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "scripts\\second.ps1", ["resourceId"] = "Kora.Second.Helper", ["kind"] = "helper",
        });
        root["helperLoadOrder"]!.AsArray().Add("scripts\\second.ps1");
        resources.Add(Resource("scripts\\second.ps1", "Kora.Second.Helper", "second"));
        resources[0] = Resource(ManifestName, ManifestId, root.ToJsonString());
        var original = new SkillPackageCatalogue([ManifestId], resources).Packages[0];
        root["helperLoadOrder"] = new JsonArray("scripts\\second.ps1", "scripts\\helper.ps1");
        resources[0] = Resource(ManifestName, ManifestId, root.ToJsonString());
        var reordered = new SkillPackageCatalogue([ManifestId], resources).Packages[0];
        reordered.Manifest.HelperLoadOrder.Should().Equal("scripts\\second.ps1", "scripts\\helper.ps1");
        reordered.ScriptSetDigest.Should().Be(original.ScriptSetDigest);
        reordered.DefinitionDigest.Should().NotBe(original.DefinitionDigest);
        reordered.DeclaredResourceDigest.Should().NotBe(original.DeclaredResourceDigest);
        Binding(original).HasObservedContentChange(Binding(reordered)).Should().BeTrue();
    }

    private static SkillResourceSnapshot Resource(string name, string id, string text) => new(name, id, Encoding.UTF8.GetBytes(text));
    private static List<SkillResourceSnapshot> Resources() =>
    [
        Resource(ManifestName, ManifestId, ManifestText),
        Resource("skills\\test\\skill.md", "Kora.Test.Instructions", "# test"),
        Resource("skills\\test\\fixture.json", "Kora.Test.Fixture", "{}"),
        Resource("scripts\\entry.ps1", "Kora.Test.Entry", "entry"),
        Resource("scripts\\helper.ps1", "Kora.Test.Helper", "helper"),
    ];

    private static ExactOperationBinding Binding(SkillPackageSnapshot package, string? change = null)
    {
        var unknown = new string('0', 64);
        return new(change is "action" ? "other" : package.Manifest.Action,
            change is "source" ? "profile" : "bundled", change is "skill" ? "other" : package.Manifest.Id,
            change is "definition" ? unknown : package.DefinitionDigest,
            change is "declared" ? unknown : package.DeclaredResourceDigest,
            unknown, unknown, unknown, unknown, unknown, unknown, unknown, new(1));
    }
}
