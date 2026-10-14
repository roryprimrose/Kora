using System.Globalization;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class SkillCatalogueSearchTests
{
    private static readonly string[] Dependencies = ["kora.test.adapter"];
    [Fact]
    public void BundledSearchUsesWholeBoundedMetadataAndExactObjectsWithoutRuntimePromotion()
    {
        var catalogue = BundledCatalogue(SkillPackageCatalogue.MaximumPackages);
        SkillCatalogueSearch.Bundled(catalogue, "", SkillInvocationFilter.All).Should().Equal(catalogue.Packages);
        SkillCatalogueSearch.Bundled(catalogue, "", SkillInvocationFilter.Unavailable).Should().Equal(catalogue.Packages);
        SkillCatalogueSearch.Bundled(catalogue, "", SkillInvocationFilter.Available).Should().BeEmpty();
        var last = catalogue.Packages[^1];
        foreach (var query in new[] { "NAME 7", "KORA.TEST7", "DESCRIPTION 7" })
        {
            SkillCatalogueSearch.Bundled(catalogue, query, SkillInvocationFilter.All)
                .Should().ContainSingle().Which.Should().BeSameAs(last);
        }
        foreach (var query in new[] { " Name 7 ", "*", "private-body", "instructions", "test.action7", "scripts" })
        {
            SkillCatalogueSearch.Bundled(catalogue, query, SkillInvocationFilter.All).Should().BeEmpty();
        }
        last.IsAvailableForInvocation.Should().BeFalse();
        catalogue.Packages.Should().HaveCount(SkillPackageCatalogue.MaximumPackages);
        ((Action)(() => BundledCatalogue(SkillPackageCatalogue.MaximumPackages + 1))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void SharedSearchIncludesUnavailableByDefaultAndSeparatesInstructionCompatibilityFromInvocation()
    {
        var source = Source();
        var valid = Snapshot(source, "first", "first", "A declared description");
        var incompatible = Snapshot(source, "second", "second", "Beyond first metadata", "```powershell\nrun.exe\n```");
        var unknown = SharedSkillSnapshot.Parse(source.Id, "path-only\\SKILL.md", [0xff], false);
        var invalidName = Snapshot(source, "invalid-name", "INVALID", "Declared readable metadata");
        var invalidDescription = Snapshot(source, "invalid-description", "invalid-description", new string('a', 1025));
        var catalogue = new SharedSkillCatalogue(source, [valid, incompatible, unknown, invalidName, invalidDescription]);
        SkillCatalogueSearch.Shared(catalogue, "", SkillInstructionFilter.All).Should().Equal(catalogue.Packages);
        SkillCatalogueSearch.Shared(catalogue, "", SkillInstructionFilter.Compatible).Should().Equal(valid);
        SkillCatalogueSearch.Shared(catalogue, "", SkillInstructionFilter.Incompatible)
            .Should().Equal(incompatible, unknown, invalidName, invalidDescription);
        foreach (var query in new[] { "SECOND", "BEYOND FIRST", incompatible.SourceQualifiedIdentity.ToUpperInvariant() })
        {
            SkillCatalogueSearch.Shared(catalogue, query, SkillInstructionFilter.All)
                .Should().ContainSingle().Which.Should().BeSameAs(incompatible);
        }
        SkillCatalogueSearch.Shared(catalogue, "Declared readable metadata", SkillInstructionFilter.All)
            .Should().ContainSingle().Which.Should().BeSameAs(invalidName);
        foreach (var query in new[] { "path-only", "SKILL.md", source.ProfileRelativeRoot, "private-body", "run.exe",
            " second ", "*", new string('a', 256) })
        {
            SkillCatalogueSearch.Shared(catalogue, query, SkillInstructionFilter.All).Should().BeEmpty();
        }
        SkillCatalogueSearch.Shared(catalogue, "second", SkillInstructionFilter.Compatible).Should().BeEmpty();
        catalogue.Packages.Should().OnlyContain(package => !package.IsAvailableForInvocation);
        SkillCatalogueSearch.Shared(new(source, []), "", SkillInstructionFilter.All).Should().BeEmpty();
    }

    [Fact]
    public void CollisionsRemainExactSourceQualifiedObjectsAndNeverMergeByName()
    {
        var first = Source();
        var second = new SharedSkillSource(Guid.NewGuid(), first.ProfileRelativeRoot, first.DirectoryIdentity);
        var one = new SharedSkillCatalogue(first, [Snapshot(first, "one", "same", "Description"), Snapshot(first, "two", "same", "Description")]);
        var two = new SharedSkillCatalogue(second, [Snapshot(second, "one", "same", "Description")]);
        var matches = SkillCatalogueSearch.Shared(one, "same", SkillInstructionFilter.All);
        matches.Should().Equal(one.Packages);
        matches.Should().OnlyContain(package => !package.IsInstructionCompatible);
        matches[0].Should().NotBeSameAs(matches[1]);
        matches[0].SourceQualifiedIdentity.Should().Be(matches[1].SourceQualifiedIdentity);
        SkillCatalogueSearch.Shared(two, matches[0].SourceQualifiedIdentity, SkillInstructionFilter.All).Should().BeEmpty();
        SkillCatalogueSearch.Shared(two, "same", SkillInstructionFilter.All)
            .Should().ContainSingle().Which.Should().BeSameAs(two.Packages[0]);
        var bundled = BundledCatalogue(1);
        var collision = new SharedSkillCatalogue(first, [Snapshot(first, "one", "kora-test0", "kora.test0")]);
        SkillCatalogueSearch.Shared(collision, "kora.test0", SkillInstructionFilter.All)
            .Should().ContainSingle().Which.SourceQualifiedIdentity.Should().StartWith("shared-profile:");
        SkillCatalogueSearch.Bundled(bundled, "kora.test0", SkillInvocationFilter.All)
            .Should().ContainSingle().Which.Should().BeSameAs(bundled.Packages[0]);
    }

    [Fact]
    public void FullSharedBoundIsSearchableWithoutTruncationAndOverBoundCatalogueIsRejected()
    {
        var source = Source();
        var packages = Enumerable.Range(0, SharedSkillCatalogue.MaximumPackages).Select(index =>
            Snapshot(source, "path" + index.ToString(CultureInfo.InvariantCulture),
                "name" + index.ToString(CultureInfo.InvariantCulture), new string('界', 1024))).ToArray();
        var catalogue = new SharedSkillCatalogue(source, packages);
        SkillCatalogueSearch.Shared(catalogue, "", SkillInstructionFilter.All).Should().Equal(packages);
        SkillCatalogueSearch.Shared(catalogue, "NAME31", SkillInstructionFilter.All)
            .Should().ContainSingle().Which.Should().BeSameAs(packages[^1]);
        ((Action)(() => _ = new SharedSkillCatalogue(source, packages.Append(Snapshot(source, "extra", "extra", "extra")).ToArray())))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NullUnsafeQueriesAndUnknownFiltersFailExplicitlyWithoutAllFallback()
    {
        var bundled = BundledCatalogue(1);
        var source = Source();
        var shared = new SharedSkillCatalogue(source, [Snapshot(source, "one", "one", "Description")]);
        ((Action)(() => SkillCatalogueSearch.Bundled(null!, "", SkillInvocationFilter.All))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SkillCatalogueSearch.Shared(null!, "", SkillInstructionFilter.All))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SkillCatalogueSearch.Bundled(bundled, null!, SkillInvocationFilter.All))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SkillCatalogueSearch.Shared(shared, null!, SkillInstructionFilter.All))).Should().Throw<ArgumentNullException>();
        ((Action)(() => SkillCatalogueSearch.Bundled(bundled, "", (SkillInvocationFilter)99))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => SkillCatalogueSearch.Shared(shared, "", (SkillInstructionFilter)99))).Should().Throw<ArgumentOutOfRangeException>();
        foreach (var query in new[] { "\n", "\u202e", new string('\ud800', 1), new string('a', 257), new string('界', 171) })
        {
            ((Action)(() => SkillCatalogueSearch.Bundled(bundled, query, SkillInvocationFilter.All))).Should().Throw<InvalidDataException>();
            ((Action)(() => SkillCatalogueSearch.Shared(shared, query, SkillInstructionFilter.All))).Should().Throw<InvalidDataException>();
        }
    }

    private static SharedSkillSource Source() => new(Guid.NewGuid(), ".agents\\skills", new string('a', 48));

    private static SharedSkillSnapshot Snapshot(SharedSkillSource source, string path, string name, string description, string body = "private-body") =>
        SharedSkillSnapshot.Parse(source.Id, path + "\\SKILL.md",
            Encoding.UTF8.GetBytes($"---\nname: {name}\nversion: 1.0.0\ndescription: {description}\n---\n{body}"), false);

    private static SkillPackageCatalogue BundledCatalogue(int count)
    {
        var resources = new List<SkillResourceSnapshot>();
        var manifests = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var id = index.ToString(CultureInfo.InvariantCulture);
            var manifestId = "Kora.Test.Manifest" + id;
            var instructionName = "skills\\test" + id + "\\skill.md";
            var entryName = "scripts\\entry" + id + ".ps1";
            var text = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, id = "kora.test" + id, version = "1.0.0", name = "Name " + id,
                description = "Description " + id, action = "test.action" + id, entryPoint = entryName,
                parameterContract = "test.v1", runtime = "powershell7", dependencies = Dependencies,
                helperLoadOrder = Array.Empty<string>(),
                files = new[]
                {
                    new { name = instructionName, resourceId = "Kora.Test.Instructions" + id, kind = "instructions" },
                    new { name = entryName, resourceId = "Kora.Test.Entry" + id, kind = "entry" },
                },
            });
            resources.Add(new("skills\\test" + id + "\\manifest.json", manifestId, Encoding.UTF8.GetBytes(text)));
            resources.Add(new(instructionName, "Kora.Test.Instructions" + id, Encoding.UTF8.GetBytes("private-body")));
            resources.Add(new(entryName, "Kora.Test.Entry" + id, Encoding.UTF8.GetBytes("private-body")));
            manifests.Add(manifestId);
        }
        return new(manifests, resources);
    }
}
