using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Testing;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class SpeechComponentTests
{
    [Fact]
    public void ProductionCatalogueHasNoInventedOffers()
    {
        SpeechComponentCatalogue.FormatVersion.Should().Be(1);
        SpeechComponentCatalogue.Production.Components.Should().BeEmpty();
        FluentActions.Invoking(() => SpeechComponentCatalogue.Production.Resolve(new("unknown", "v1")))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CatalogueFreezesExactMetadataAndPinsCompleteManifest()
    {
        var definition = SpeechComponentFixture.Definition();
        var entries = new[] { definition };
        var catalogue = new SpeechComponentCatalogue(entries);
        entries[0] = SpeechComponentFixture.Definition(null);
        catalogue.Resolve(definition.Identity).Should().BeSameAs(definition);
        definition.Identity.StorageName.Should().Be("component-fixture-input.v1");
        definition.DownloadBytes.Should().Be(8);
        definition.Assets.Should().HaveCount(2);
        definition.ManifestDigest.Should().HaveLength(64);
        SpeechComponentFixture.Definition().ManifestDigest.Should().Be(definition.ManifestDigest);
        SpeechComponentFixture.Definition(null).ManifestDigest.Should().NotBe(definition.ManifestDigest);
        SpeechComponentFixture.Definition(ram: 33).ManifestDigest.Should().NotBe(definition.ManifestDigest);
        definition.RuntimeProfile.Should().Be("fixture-runtime-v1");
        definition.ModelIdentity.Should().Be("fixture-model-v1");
        definition.Licence.Should().Contain("Fixture");
        definition.LicenceSource.Host.Should().Be("speech.example.invalid");
        definition.StagingBytes.Should().Be(16);
        definition.InstalledBytes.Should().Be(8);
        definition.RamBytes.Should().Be(32);
        definition.VramBytes.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../outside")]
    [InlineData("CAPS")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("a.b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    public void IdentityRejectsUnsafeTokens(string token)
    {
        FluentActions.Invoking(() => new SpeechComponentIdentity(token, "v1")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new SpeechComponentIdentity("fixture", token)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IdentityRejectsOverlongTokens()
    {
        FluentActions.Invoking(() => new SpeechComponentIdentity(new string('a', 65), "v1")).Should().Throw<ArgumentException>();
        new SpeechComponentIdentity(new string('a', 64), "v1").ComponentId.Should().HaveLength(64);
    }

    [Theory]
    [InlineData("../model.data")]
    [InlineData("..\\model.data")]
    [InlineData("C:model.data")]
    [InlineData("model.dll")]
    [InlineData("model.exe")]
    [InlineData("model.zip")]
    [InlineData("model.data:stream")]
    [InlineData("model..data")]
    [InlineData("con.data")]
    [InlineData("com1.data")]
    [InlineData("lpt9.data")]
    [InlineData(".data")]
    [InlineData("model.DATA")]
    public void AssetsRejectPathsNativeCodeAndArchives(string name) =>
        FluentActions.Invoking(() => SpeechComponentFixture.Asset(name)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("http://speech.example.invalid/model.data")]
    [InlineData("https://localhost/model.data")]
    [InlineData("https://127.0.0.1/model.data")]
    [InlineData("https://speech.example.invalid:444/model.data")]
    [InlineData("https://user:password@speech.example.invalid/model.data")]
    [InlineData("https://speech.example.invalid/model.data?token=secret")]
    [InlineData("https://speech.example.invalid/model.data#fragment")]
    [InlineData("file:///C:/model.data")]
    [InlineData("relative.data")]
    public void AssetsRejectUnapprovedSourceShapes(string source) =>
        FluentActions.Invoking(() => new SpeechComponentAsset("model.data", new Uri(source, UriKind.RelativeOrAbsolute),
            4, new string('A', 64))).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("bad")]
    [InlineData("")]
    [InlineData("abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd")]
    public void AssetsRequireExactCanonicalDigest(string hash) =>
        FluentActions.Invoking(() => new SpeechComponentAsset("model.data",
            new Uri("https://speech.example.invalid/model.data"), 4, hash)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AssetsRequireExactPositiveSize(long size) =>
        FluentActions.Invoking(() => new SpeechComponentAsset("model.data",
            new Uri("https://speech.example.invalid/model.data"), size, new string('A', 64)))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void CatalogueRejectsDuplicateIdentityAndOverBudgetEntries()
    {
        var definition = SpeechComponentFixture.Definition();
        FluentActions.Invoking(() => new SpeechComponentCatalogue([definition, definition])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new SpeechComponentCatalogue(Enumerable.Range(0, 33)
            .Select(index => SpeechComponentFixture.Definition(identity: new("fixture", "v" + index)))))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefinitionRejectsMissingDuplicateAndTooManyAssets()
    {
        FluentActions.Invoking(() => SpeechComponentFixture.Definition(assets: [])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SpeechComponentFixture.Definition(assets: [SpeechComponentFixture.Asset("a.data"), SpeechComponentFixture.Asset("a.data")]))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SpeechComponentFixture.Definition(assets: Enumerable.Range(0, 17)
            .Select(index => SpeechComponentFixture.Asset("asset" + index + ".data")))).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(4, 8, 32, 0)]
    [InlineData(16, 4, 32, 0)]
    [InlineData(16, 8, -1, 0)]
    [InlineData(16, 8, 32, -1)]
    public void DefinitionRejectsInvalidResourceCosts(long staging, long installed, long ram, long vram) =>
        FluentActions.Invoking(() => SpeechComponentFixture.Definition(staging: staging, installed: installed, ram: ram, vram: vram))
            .Should().Throw<ArgumentException>();

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void OfferAdmissionKeepsCompatibilityAndQualificationDistinct(bool compatible, bool qualified, bool offered)
    {
        var state = new SpeechComponentReadiness(new("fixture", "v1"),
            new(false, false, SpeechComponentOwnership.Missing, "missing"), compatible, qualified);
        state.CanOffer.Should().Be(offered);
        state.FunctionalVerified.Should().BeFalse();
        state.Storage.Installed.Should().BeFalse();
    }
}
