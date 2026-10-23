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
    [InlineData("prn.data")]
    [InlineData("aux.data")]
    [InlineData("nul.data")]
    [InlineData("com1.data")]
    [InlineData("com2.data")]
    [InlineData("com3.data")]
    [InlineData("com4.data")]
    [InlineData("com5.data")]
    [InlineData("com6.data")]
    [InlineData("com7.data")]
    [InlineData("com8.data")]
    [InlineData("com9.data")]
    [InlineData("lpt1.data")]
    [InlineData("lpt2.data")]
    [InlineData("lpt3.data")]
    [InlineData("lpt4.data")]
    [InlineData("lpt5.data")]
    [InlineData("lpt6.data")]
    [InlineData("lpt7.data")]
    [InlineData("lpt8.data")]
    [InlineData("lpt9.data")]
    [InlineData("overlong-model-file-name-that-exceeds-the-sixty-four-character-budget.data")]
    [InlineData(".data")]
    [InlineData("model.DATA")]
    public void AssetsRejectPathsNativeCodeAndArchives(string name) =>
        FluentActions.Invoking(() => SpeechComponentFixture.Asset(name)).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("http://speech.example.invalid/model.data")]
    [InlineData("https://localhost/model.data")]
    [InlineData("https://127.0.0.1/model.data")]
    [InlineData("https://192.0.2.1/model.data")]
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
        FluentActions.Invoking(() => new SpeechComponentCatalogue([null!])).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefinitionRejectsMissingDuplicateAndTooManyAssets()
    {
        FluentActions.Invoking(() => SpeechComponentFixture.Definition(assets: [])).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SpeechComponentFixture.Definition(assets: [SpeechComponentFixture.Asset("a.data"), SpeechComponentFixture.Asset("a.data")]))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SpeechComponentFixture.Definition(assets: Enumerable.Range(0, 17)
            .Select(index => SpeechComponentFixture.Asset("asset" + index + ".data")))).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SpeechComponentFixture.Definition(assets: [null!])).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("name-length")]
    [InlineData("model-length")]
    [InlineData("licence-length")]
    [InlineData("name-control")]
    [InlineData("model-control")]
    [InlineData("licence-control")]
    [InlineData("relative-licence")]
    [InlineData("http-licence")]
    [InlineData("licence-user")]
    [InlineData("licence-fragment")]
    public void DefinitionRejectsInvalidDisplayAndLicenceMetadata(string invalid)
    {
        var name = invalid is "name-length" ? new string('a', 129) : invalid is "name-control" ? "name\nvalue" : "Fixture";
        var model = invalid is "model-length" ? new string('a', 257) : invalid is "model-control" ? "model\nvalue" : "fixture-model";
        var licence = invalid is "licence-length" ? new string('a', 257) : invalid is "licence-control" ? "licence\nvalue" : "Fixture licence";
        var licenceUri = invalid switch
        {
            "relative-licence" => new Uri("licence.txt", UriKind.Relative),
            "http-licence" => new Uri("http://speech.example.invalid/licence"),
            "licence-user" => new Uri("https://fixture@speech.example.invalid/licence"),
            "licence-fragment" => new Uri("https://speech.example.invalid/licence#fragment"),
            _ => new Uri("https://speech.example.invalid/licence"),
        };
        FluentActions.Invoking(() => new SpeechComponentDefinition(new("fixture", "v1"), name, "fixture-runtime",
            model, licence, licenceUri, "fixture-qualified", 16, 8, 32, 0,
            [SpeechComponentFixture.Asset("model.data")])).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("model.bin")]
    [InlineData("model.onnx")]
    [InlineData("model.json")]
    [InlineData("model.txt")]
    [InlineData("model.model")]
    [InlineData("model.data")]
    public void AssetsAcceptOnlyExplicitDataExtensionsWithoutChangingTheirIdentity(string name) =>
        SpeechComponentFixture.Asset(name).Name.Should().Be(name);

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
            new(false, false, SpeechComponentOwnership.Missing, "missing"),
            new(compatible ? SpeechComponentCompatibility.Compatible : SpeechComponentCompatibility.Incompatible,
                SpeechComponentFunctionalEvidence.NotChecked, "Fixture-only observed compatibility."), qualified);
        state.CanOffer.Should().Be(offered);
        state.Runtime.Functional.Should().Be(SpeechComponentFunctionalEvidence.NotChecked);
        state.Storage?.Installed.Should().BeFalse();
    }

    [Fact]
    public void UnknownStorageDoesNotUseFalseOrMissingAsAReadinessFallback()
    {
        var state = new SpeechComponentReadiness(new("fixture", "v1"), null,
            new(SpeechComponentCompatibility.Unknown, SpeechComponentFunctionalEvidence.Unknown, "Unknown exact host evidence."),
            true, "Inspect ownership before retrying.");
        state.CanOffer.Should().BeFalse();
        state.Storage.Should().BeNull();
        state.Description.Should().Contain("Local data state: Unknown").And.Contain("Inspect ownership");
    }

    [Theory]
    [InlineData(SpeechComponentMutationState.Unavailable)]
    [InlineData(SpeechComponentMutationState.Busy)]
    [InlineData(SpeechComponentMutationState.UnknownOwnership)]
    [InlineData(SpeechComponentMutationState.SharedOrUserOwned)]
    [InlineData(SpeechComponentMutationState.Denied)]
    public void MutationAdmissionRequiresExactLeaseAndExposesDistinctDenialReasons(SpeechComponentMutationState state)
    {
        var denied = new SpeechComponentMutationAdmission(state);
        denied.State.Should().Be(state);
        denied.Lease.Should().BeNull();
        denied.Detail.Should().NotBeNullOrWhiteSpace();
        denied.ReasonCode.Should().NotBeNullOrWhiteSpace();
        FluentActions.Invoking(() => denied.RequireLease()).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new SpeechComponentMutationAdmission(state, new FixtureLease()))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AcquiredAdmissionCarriesOnlyItsOwnedLease()
    {
        var owned = new FixtureLease();
        var admission = new SpeechComponentMutationAdmission(SpeechComponentMutationState.Acquired, owned);
        admission.Lease.Should().BeSameAs(owned);
        admission.RequireLease().Should().BeSameAs(owned);
        admission.ReasonCode.Should().Be("exclusive-ownership-confirmed");
        admission.Detail.Should().Contain("Exclusive");
        await owned.DisposeAsync();
        owned.Released.Should().BeTrue();
        FluentActions.Invoking(() => new SpeechComponentMutationAdmission(SpeechComponentMutationState.Acquired))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new SpeechComponentMutationAdmission((SpeechComponentMutationState)99))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("private\nbody")]
    public void RuntimeObservationRequiresExplicitBoundedReasons(string detail) =>
        FluentActions.Invoking(() => new SpeechComponentRuntimeObservation(SpeechComponentCompatibility.Unknown,
            SpeechComponentFunctionalEvidence.Unknown, detail)).Should().Throw<ArgumentException>();

    [Fact]
    public void RuntimeObservationRejectsUnknownEnumValuesAndOverBudgetReasons()
    {
        FluentActions.Invoking(() => new SpeechComponentRuntimeObservation((SpeechComponentCompatibility)99,
            SpeechComponentFunctionalEvidence.NotChecked, "Reason")).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new SpeechComponentRuntimeObservation(SpeechComponentCompatibility.Unknown,
            (SpeechComponentFunctionalEvidence)99, "Reason")).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new SpeechComponentRuntimeObservation(SpeechComponentCompatibility.Unknown,
            SpeechComponentFunctionalEvidence.Unknown, new string('a', 513))).Should().Throw<ArgumentException>();
    }

    private sealed class FixtureLease : IAsyncDisposable
    {
        internal bool Released { get; private set; }
        public ValueTask DisposeAsync() { Released = true; return ValueTask.CompletedTask; }
    }
}
