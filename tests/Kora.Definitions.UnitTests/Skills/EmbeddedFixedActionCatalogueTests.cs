using AwesomeAssertions;

using Kora.Core.Skills;
using Kora.Definitions.Skills;

namespace Kora.Definitions.UnitTests.Skills;

public sealed class EmbeddedFixedActionCatalogueTests
{
    [Theory]
    [InlineData("kora.session.lock", FixedSessionAction.Lock)]
    [InlineData("kora.computer.shutdown", FixedSessionAction.Shutdown)]
    [InlineData("kora.computer.restart", FixedSessionAction.Restart)]
    public void Only_exact_known_packages_resolve_to_host_selected_effects(string id, FixedSessionAction expected)
    {
        var package = EmbeddedSkillCatalogue.Load().Packages.Single(item => string.Equals(item.Manifest.Id, id, StringComparison.Ordinal));
        var plan = EmbeddedFixedActionCatalogue.Resolve(package);
        plan.Package.Should().BeSameAs(package);
        plan.Action.Should().Be(expected);
    }

    [Fact]
    public void Null_and_unknown_effect_packages_are_not_admitted()
    {
        ((Action)(() => EmbeddedFixedActionCatalogue.Resolve(null!))).Should().Throw<ArgumentNullException>();
        var catalogue = EmbeddedSkillCatalogue.Load();
        var files = catalogue.Packages.SelectMany(item => item.Files).Distinct().ToList();
        var manifest = files.Single(item => string.Equals(item.ResourceId, "Kora.Skills.Session.Lock.Manifest", StringComparison.Ordinal));
        files[files.IndexOf(manifest)] = new(manifest.Name, manifest.ResourceId,
            System.Text.Encoding.UTF8.GetBytes(manifest.Text.Replace("kora.session.lock", "kora.other.lock", StringComparison.Ordinal)));
        var changed = new SkillPackageCatalogue(catalogue.Packages.Select(item => item.Files[0].ResourceId).ToArray(), files);
        ((Action)(() => EmbeddedFixedActionCatalogue.Resolve(changed.Packages[0]))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Any_declared_file_change_invalidates_the_closed_effect_registration()
    {
        var original = EmbeddedSkillCatalogue.Load();
        foreach (var target in original.Packages[0].Files)
        {
            var files = original.Packages.SelectMany(item => item.Files).Distinct().ToList();
            files[files.IndexOf(target)] = new(target.Name, target.ResourceId, [.. target.Bytes.ToArray(), 0x20]);
            var changed = new SkillPackageCatalogue(original.Packages.Select(item => item.Files[0].ResourceId).ToArray(), files);
            ((Action)(() => EmbeddedFixedActionCatalogue.Resolve(changed.Packages[0]))).Should().Throw<InvalidDataException>();
        }
    }
}