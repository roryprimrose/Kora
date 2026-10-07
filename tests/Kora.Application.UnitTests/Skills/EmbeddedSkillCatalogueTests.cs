using System.Security.Cryptography;

using AwesomeAssertions;

using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora.Application.UnitTests.Skills;

public sealed class EmbeddedSkillCatalogueTests
{
    [Theory]
    [InlineData("kora.session.lock", "4b17598f9ffc1b9b0c93937f199ae3c98b46d3971b1cb396ef20a7f45268335c",
        "eff78c161cafe57936ab2c48995ee56be4ecbb498777949bee0b92696af62d0f", "d93d9e99fb9c6260295b757a60fa80c98b6098af60e7f588d6c20bc13156497b")]
    [InlineData("kora.computer.shutdown", "536403301c0760d5a2925820a50d5dfee8f9ccc908647e49e1016996c283487f",
        "d3fbc1c744dbd26079fd64b35b04339e608328ef596d7a3680fb8b73634d4a4d", "7f5237b43dfc9ebd761e3b5e148ee1764f8eef6afbc48b5070985168fa90daed")]
    [InlineData("kora.computer.restart", "7af12091a9045e79db1bdbe32715cfbfb93f2265fa5d659278778e234f84b375",
        "15bc6c610e848b844831f13d47014bb55aff3508323c5606a76e9009ea5b9ab8", "5a83b4286585bf3c478929bed4c6cd5e8fc20ff621ebb0d2c3439058d06a235d")]
    public void Real_fixed_packages_match_independent_golden_vectors_and_review_original_embedded_bytes(
        string id, string scripts, string definition, string declared)
    {
        var catalogue = EmbeddedSkillCatalogue.Load();
        catalogue.Packages.Should().HaveCount(3);
        var package = catalogue.Packages.Single(item => string.Equals(item.Manifest.Id, id, StringComparison.Ordinal));
        package.ScriptSetDigest.Should().Be(scripts);
        package.DefinitionDigest.Should().Be(definition);
        package.DeclaredResourceDigest.Should().Be(declared);
        package.IsAvailableForInvocation.Should().BeFalse();
        package.Manifest.IsActionAvailableForInvocation.Should().BeFalse();
        package.Files.Should().HaveCount(5);
        var assembly = typeof(EmbeddedSkillCatalogue).Assembly;
        foreach (var file in package.Files)
        {
            using var stream = assembly.GetManifestResourceStream(file.ResourceId)!;
            using var output = new MemoryStream();
            stream.CopyTo(output);
            file.Bytes.ToArray().Should().Equal(output.ToArray());
            file.Digest.Should().Be(Convert.ToHexStringLower(SHA256.HashData(output.ToArray())));
        }
        catalogue.Dependents("Kora.Scripts.Shared.SessionControl").Should().HaveCount(3);
        catalogue.Packages.Select(item => item.Files.Single(file =>
            string.Equals(file.ResourceId, "Kora.Scripts.Shared.SessionControl", StringComparison.Ordinal)))
            .Distinct().Should().ContainSingle();
    }

    [Fact]
    public void Resource_loading_fails_closed_for_missing_extra_empty_oversized_or_unreadable_resources()
    {
        var assembly = typeof(EmbeddedSkillCatalogue).Assembly;
        var names = assembly.GetManifestResourceNames();
        ((Action)(() => EmbeddedSkillCatalogue.LoadResources([], assembly.GetManifestResourceStream)))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => EmbeddedSkillCatalogue.LoadResources([.. names, "Kora.Skills.Extra"], assembly.GetManifestResourceStream)))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => EmbeddedSkillCatalogue.LoadResources([.. names, "Kora.Skills.Session.Lock.Manifest"], assembly.GetManifestResourceStream)))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => EmbeddedSkillCatalogue.LoadResources(names, _ => null))).Should().Throw<InvalidDataException>();
        ((Action)(() => EmbeddedSkillCatalogue.LoadResources(names, _ => new MemoryStream()))).Should().Throw<InvalidDataException>();
        ((Action)(() => EmbeddedSkillCatalogue.LoadResources(names, _ => new MemoryStream(new byte[SkillResourceSnapshot.MaximumBytes + 1]))))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => EmbeddedSkillCatalogue.LoadResources(names, _ => throw new IOException("read failed"))))
            .Should().Throw<IOException>();
    }

    [Fact]
    public void Shared_helper_changes_every_dependent_set_but_not_unrelated_definitions()
    {
        var original = EmbeddedSkillCatalogue.Load();
        var files = original.Packages.SelectMany(package => package.Files).Distinct().ToList();
        var helper = files.Single(file => string.Equals(file.ResourceId, "Kora.Scripts.Shared.SessionControl", StringComparison.Ordinal));
        files[files.IndexOf(helper)] = new(helper.Name, helper.ResourceId, [.. helper.Bytes.ToArray(), 0x20]);
        var changed = new SkillPackageCatalogue(original.Packages.Select(package => package.Files[0].ResourceId).ToArray(), files);
        for (var i = 0; i < original.Packages.Count; i++)
        {
            changed.Packages[i].ScriptSetDigest.Should().NotBe(original.Packages[i].ScriptSetDigest);
            changed.Packages[i].DeclaredResourceDigest.Should().NotBe(original.Packages[i].DeclaredResourceDigest);
            changed.Packages[i].DefinitionDigest.Should().Be(original.Packages[i].DefinitionDigest);
        }
    }
}
