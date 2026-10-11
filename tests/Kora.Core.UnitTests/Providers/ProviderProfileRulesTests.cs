using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Providers;

public sealed class ProviderProfileRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ProviderProfileOption Option(string id = "fixed") => new(new(id), "Provider managed", true);
    private static ProviderCatalogueModel Model(ProviderChoiceId? id = null, ModelProviderSelection? selection = null,
        IReadOnlyList<ProviderProfileOption>? reasoning = null, IReadOnlyList<ProviderProfileOption>? context = null) =>
        new(id ?? new("exact-model"), "Advertised model", selection ?? ModelProviderSelection.OllamaCandidate,
            reasoning ?? [Option()], context ?? [Option()]);
    private static ProviderCapabilityCatalogue Catalogue(IReadOnlyList<ProviderCatalogueModel>? models = null) =>
        new(ProviderService.Local, ProviderCatalogueStatus.Current, new("account"), new(1), Now.AddMinutes(-1), Now.AddMinutes(1), models ?? [Model()]);
    private static ProviderProfileTuple Tuple() => new(ProviderService.Local, new("account"), new(1), new("exact-model"), new("fixed"), new("fixed"));

    [Theory]
    [InlineData(ProviderProfileField.Model)]
    [InlineData(ProviderProfileField.Reasoning)]
    [InlineData(ProviderProfileField.Context)]
    public void IndividualFieldReplacementPreservesCompleteExactShape(ProviderProfileField field)
    {
        var before = Tuple();
        var id = new ProviderChoiceId("replacement");
        var after = ProviderProfileRules.ReplaceField(before, field, id);
        after.Service.Should().Be(before.Service);
        after.Account.Should().Be(before.Account);
        after.CatalogueRevision.Should().Be(before.CatalogueRevision);
        after.Model.Should().Be(field == ProviderProfileField.Model ? id : before.Model);
        after.Reasoning.Should().Be(field == ProviderProfileField.Reasoning ? id : before.Reasoning);
        after.Context.Should().Be(field == ProviderProfileField.Context ? id : before.Context);
        FluentActions.Invoking(() => ProviderProfileRules.ValidateTuple(after)).Should().NotThrow();
    }

    [Fact]
    public void IndividualFieldReplacementRejectsMissingOrUnknownValues()
    {
        FluentActions.Invoking(() => ProviderProfileRules.ReplaceField(null!, ProviderProfileField.Model, new("exact"))).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => ProviderProfileRules.ReplaceField(Tuple(), ProviderProfileField.Model, null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ProviderProfileRules.ReplaceField(Tuple(), (ProviderProfileField)99, new("exact"))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("two ids")]
    [InlineData("\u0001")]
    [InlineData("é")]
    public void InvalidIdsFailClosed(string? value)
    {
        FluentActions.Invoking(() => new ProviderChoiceId(value!)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ProviderAccountId(value!)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ExactIdBoundsAndTypedValuesArePreserved()
    {
        var text = new string('a', 128);
        new ProviderChoiceId(text).Value.Should().Be(text);
        new ProviderAccountId(text).Value.Should().Be(text);
        FluentActions.Invoking(() => new ProviderChoiceId(text + "a")).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ProviderAccountId(text + "a")).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("service")]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    public void IncompleteTupleIsNotAConfiguredProfile(string field)
    {
        var tuple = field switch
        {
            "null" => null,
            "service" => Tuple() with { Service = ProviderService.Unset },
            "account" => Tuple() with { Account = null! },
            "revision" => Tuple() with { CatalogueRevision = default },
            "model" => Tuple() with { Model = null! },
            "reasoning" => Tuple() with { Reasoning = null! },
            _ => Tuple() with { Context = null! },
        };
        FluentActions.Invoking(() => ProviderProfileRules.ValidateTuple(tuple)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("unavailable")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("service")]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    public void ResolutionRequiresCurrentCompleteExactAccountCatalogueTuple(string field)
    {
        var catalogue = Catalogue();
        var tuple = Tuple();
        if (field is "null") { tuple = null; }
        if (field is "unavailable") { catalogue = new(ProviderService.Local, ProviderCatalogueStatus.Unavailable, null, new(1), Now.AddMinutes(-1), Now.AddMinutes(1), []); }
        if (field is "future") { catalogue = new(ProviderService.Local, ProviderCatalogueStatus.Current, new("account"), new(1), Now.AddSeconds(1), Now.AddMinutes(1), [Model()]); }
        if (field is "service") { tuple = tuple! with { Service = ProviderService.Claude }; }
        if (field is "account") { tuple = tuple! with { Account = new("other-account") }; }
        if (field is "revision") { tuple = tuple! with { CatalogueRevision = new(2) }; }
        if (field is "model") { tuple = tuple! with { Model = new("unknown") }; }
        if (field is "reasoning") { tuple = tuple! with { Reasoning = new("unsupported-managed") }; }
        if (field is "context") { tuple = tuple! with { Context = new("unsupported-context") }; }
        ProviderProfileRules.Resolve(tuple, catalogue, field is "expired" ? Now.AddMinutes(1) : Now).Should().BeNull();
    }

    [Theory]
    [InlineData(ProviderService.Local, ModelProviderIdentity.Ollama)]
    [InlineData(ProviderService.Copilot, ModelProviderIdentity.Copilot)]
    [InlineData(ProviderService.Claude, ModelProviderIdentity.Claude)]
    [InlineData(ProviderService.Codex, ModelProviderIdentity.Codex)]
    public void EveryServiceUsesItsOwnExactHostSelectionAndImmutableChoices(ProviderService service, ModelProviderIdentity provider)
    {
        var selection = ModelProviderSelection.OllamaCandidate with { Provider = provider };
        var options = new[] { new ProviderProfileOption(new("selectable"), "Exact preset", false, 4096) };
        var models = new[] { Model(selection: selection, context: options) };
        var catalogue = new ProviderCapabilityCatalogue(service, ProviderCatalogueStatus.Current, new("account"), new(1), Now, Now.AddMinutes(1), models);
        models[0] = Model();
        options[0] = Option("replacement");
        var tuple = Tuple() with { Service = service, Context = new("selectable") };
        ProviderProfileRules.Resolve(tuple, catalogue, Now)!.Selection.Should().Be(selection);
        catalogue.Models[0].Context[0].InputTokenLimit.Should().Be(4096);
        catalogue.Models[0].Context[0].ProviderManaged.Should().BeFalse();
        catalogue.Models[0].Reasoning[0].ProviderManaged.Should().BeTrue();
        ProviderProfileRules.ServiceFor(provider).Should().Be(service);
        ProviderProfileRules.ServiceFor((ModelProviderIdentity)99).Should().Be(ProviderService.Unset);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("status")]
    [InlineData("revision")]
    [InlineData("interval")]
    [InlineData("capacity")]
    [InlineData("duplicate")]
    [InlineData("duplicate-mapping")]
    [InlineData("unavailable-model")]
    [InlineData("account")]
    [InlineData("model-id")]
    [InlineData("selection")]
    [InlineData("cross-service")]
    [InlineData("empty-reasoning")]
    [InlineData("empty-context")]
    [InlineData("option-capacity")]
    [InlineData("duplicate-option")]
    [InlineData("mixed-managed")]
    [InlineData("option-id")]
    [InlineData("zero-tokens")]
    [InlineData("negative-tokens")]
    [InlineData("reasoning-tokens")]
    [InlineData("managed-tokens")]
    [InlineData("blank-model-label")]
    [InlineData("long-model-label")]
    [InlineData("control-model-label")]
    [InlineData("blank-option-label")]
    public void InconsistentOrUnsupportedCatalogueIsRejected(string field)
    {
        var models = new List<ProviderCatalogueModel> { Model() };
        var service = ProviderService.Local;
        var status = ProviderCatalogueStatus.Current;
        ProviderAccountId? account = new("account");
        var revision = 1L;
        var expiry = Now.AddMinutes(1);
        switch (field)
        {
            case "service": service = ProviderService.Unset; break;
            case "status": status = (ProviderCatalogueStatus)99; break;
            case "revision": revision = 0; break;
            case "interval": expiry = Now; break;
            case "capacity": models = Enumerable.Range(0, 65).Select(index => Model(new(index.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ToList(); break;
            case "duplicate": models.Add(models[0]); break;
            case "duplicate-mapping": models.Add(Model(new("other-model"))); break;
            case "unavailable-model": status = ProviderCatalogueStatus.Unavailable; break;
            case "account": account = null; break;
            case "model-id": models[0] = new(null!, "Model", ModelProviderSelection.OllamaCandidate, [Option()], [Option()]); break;
            case "selection": models[0] = Model(selection: ModelProviderSelection.OllamaCandidate with { Model = default }); break;
            case "cross-service": models[0] = Model(selection: ModelProviderSelection.CopilotCandidate); break;
            case "empty-reasoning": models[0] = Model(reasoning: []); break;
            case "empty-context": models[0] = Model(context: []); break;
            case "option-capacity": models[0] = Model(context: Enumerable.Range(0, 17).Select(index => Option(index.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray()); break;
            case "duplicate-option": models[0] = Model(reasoning: [Option(), Option()]); break;
            case "mixed-managed": models[0] = Model(reasoning: [Option(), Option("other") with { ProviderManaged = false }]); break;
            case "option-id": models[0] = Model(context: [new(null!, "Choice", false)]); break;
            case "zero-tokens": models[0] = Model(context: [new(new("choice"), "Choice", false, 0)]); break;
            case "negative-tokens": models[0] = Model(context: [new(new("choice"), "Choice", false, -1)]); break;
            case "reasoning-tokens": models[0] = Model(reasoning: [new(new("choice"), "Choice", false, 4096)]); break;
            case "managed-tokens": models[0] = Model(context: [new(new("choice"), "Choice", true, 4096)]); break;
            case "blank-model-label": models[0] = new(new("model"), " ", ModelProviderSelection.OllamaCandidate, [Option()], [Option()]); break;
            case "long-model-label": models[0] = new(new("model"), new string('x', 257), ModelProviderSelection.OllamaCandidate, [Option()], [Option()]); break;
            case "control-model-label": models[0] = new(new("model"), "Model\n", ModelProviderSelection.OllamaCandidate, [Option()], [Option()]); break;
            case "blank-option-label": models[0] = Model(context: [new(new("choice"), "", false)]); break;
        }
        FluentActions.Invoking(() => new ProviderCapabilityCatalogue(service, status, account, revision == 0 ? default : new(revision), Now, expiry, models))
            .Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ObservationRequiresExactlyAllServicesAndSnapshotsItsInventory()
    {
        var catalogues = Enum.GetValues<ProviderService>().Where(service => service != ProviderService.Unset)
            .Select(service => new ProviderCapabilityCatalogue(service, ProviderCatalogueStatus.AuthenticationRequired, null, new(1), Now, Now.AddMinutes(1), [])).ToArray();
        var observed = new ProviderCatalogueObservation(LocalProviderInstallation.Installed, catalogues);
        catalogues[0] = Catalogue();
        observed.For(ProviderService.Local).Status.Should().Be(ProviderCatalogueStatus.AuthenticationRequired);
        observed.Installation.Should().Be(LocalProviderInstallation.Installed);
        observed.Catalogues.Should().HaveCount(4);
        FluentActions.Invoking(() => new ProviderCatalogueObservation((LocalProviderInstallation)99, catalogues)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ProviderCatalogueObservation(LocalProviderInstallation.Unknown, [])).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ProviderCatalogueObservation(LocalProviderInstallation.Unknown, [catalogues[0], catalogues[0], catalogues[1], catalogues[2]])).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MultipleSelectableOptionsHaveExactDistinctIdsAndNotManagedSubstitutes()
    {
        var model = Model(reasoning: [Option("low") with { ProviderManaged = false }, Option("high") with { ProviderManaged = false }]);
        Catalogue([model]).Models[0].Reasoning.Should().HaveCount(2);
    }
}
