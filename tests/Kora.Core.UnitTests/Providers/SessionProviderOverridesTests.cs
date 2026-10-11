using AwesomeAssertions;
using Kora.Core.Providers;

namespace Kora.Core.UnitTests.Providers;

public sealed class SessionProviderOverridesTests
{
    [Theory]
    [InlineData(SessionProviderField.Model)]
    [InlineData(SessionProviderField.Reasoning)]
    [InlineData(SessionProviderField.Context)]
    public void ExactFieldSelectionKeepsUnchangedOverridesAndInitialSparseInheritance(SessionProviderField field)
    {
        var tuple = Tuple();
        var selected = SessionProviderOverrides.Inherit.Select(tuple, field);
        selected.Model.Should().Be(field == SessionProviderField.Model ? tuple.Model : null);
        selected.Reasoning.Should().Be(field == SessionProviderField.Reasoning ? tuple.Reasoning : null);
        selected.Context.Should().Be(field == SessionProviderField.Context ? tuple.Context : null);
        selected.Select(tuple, field).Serialize().Should().Be(selected.Serialize());
        var full = SessionProviderOverrides.Inherit.Select(tuple, null);
        full.Select(tuple, field).Serialize().Should().Be(full.Serialize());
        new SessionProviderOverrides(context: tuple.Context, binding: tuple)
            .Select(tuple, field).Context.Should().Be(tuple.Context);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    public void FieldSelectionCannotSilentlyRebindOtherOverridesEvenWhenOpaqueIdsMatch(string drift)
    {
        var original = Tuple();
        var changed = drift switch
        {
            "service" => Tuple(ProviderService.Claude),
            "account" => Tuple(account: "other"),
            "revision" => Tuple(revision: 2),
            _ => Tuple(model: "other"),
        };
        var policy = SessionProviderOverrides.Inherit.Select(original, null);
        ((Action)(() => policy.Select(changed, SessionProviderField.Reasoning))).Should().Throw<InvalidDataException>();
        policy.Select(changed, null).Binding.Should().Be(changed);
        new SessionProviderOverrides(reasoning: original.Reasoning, binding: original)
            .Select(changed, SessionProviderField.Reasoning).Binding.Should().Be(changed);
    }

    [Theory]
    [InlineData(SessionProviderField.Service)]
    [InlineData((SessionProviderField)99)]
    public void TupleSelectionRejectsUnknownOrServiceField(SessionProviderField field) =>
        ((Action)(() => SessionProviderOverrides.Inherit.Select(Tuple(), field))).Should().Throw<InvalidDataException>();
    private static ProviderProfileTuple Tuple(ProviderService service = ProviderService.Local, string account = "account",
        string model = "model", string reasoning = "reasoning", string context = "context", long revision = 1) =>
        new(service, new(account), new(revision), new(model), new(reasoning), new(context));

    [Theory]
    [InlineData(ProviderService.Unset)]
    [InlineData(ProviderService.Local)]
    [InlineData(ProviderService.Claude)]
    [InlineData(ProviderService.Copilot)]
    [InlineData(ProviderService.Codex)]
    public void EmptyPolicyInheritsAllFourFieldsWithoutFabricatingChoices(ProviderService service)
    {
        var app = new ProviderProfilePreferences(service, [Tuple(), Tuple(ProviderService.Claude), Tuple(ProviderService.Copilot), Tuple(ProviderService.Codex)]);
        var policy = SessionProviderOverrides.Parse(SessionProviderOverrides.Inherit.Serialize());
        policy.Service.Should().BeNull();
        policy.Model.Should().BeNull();
        policy.Reasoning.Should().BeNull();
        policy.Context.Should().BeNull();
        policy.Binding.Should().BeNull();
        policy.EffectiveService(app).Should().Be(service);
        policy.ResolveDesired(app).Should().Be(service == ProviderService.Unset ? null : app.For(service));
    }

    [Fact]
    public void ServiceOverrideUsesEffectiveServicesAppTupleAndNeverWritesApp()
    {
        var local = Tuple();
        var hosted = Tuple(ProviderService.Claude, model: "another");
        var app = new ProviderProfilePreferences(ProviderService.Local, [local, hosted]);
        var bytes = app.Serialize();
        var policy = new SessionProviderOverrides(ProviderService.Claude);
        policy.ResolveDesired(app).Should().Be(hosted);
        policy.ResolveDesired(new(ProviderService.Local, [local])).Should().BeNull();
        app.Serialize().Should().Be(bytes);
        policy.Reset(SessionProviderField.Service).ResolveDesired(app).Should().Be(local);
    }

    [Theory]
    [InlineData(SessionProviderField.Service)]
    [InlineData(SessionProviderField.Model)]
    [InlineData(SessionProviderField.Reasoning)]
    [InlineData(SessionProviderField.Context)]
    public void IndividualResetClearsOnlySpecifiedField(SessionProviderField field)
    {
        var tuple = Tuple();
        var original = new SessionProviderOverrides(tuple.Service, tuple.Model, tuple.Reasoning, tuple.Context, tuple);
        var reset = original.Reset(field);
        reset.Service.Should().Be(field == SessionProviderField.Service ? null : original.Service);
        reset.Model.Should().Be(field == SessionProviderField.Model ? null : original.Model);
        reset.Reasoning.Should().Be(field == SessionProviderField.Reasoning ? null : original.Reasoning);
        reset.Context.Should().Be(field == SessionProviderField.Context ? null : original.Context);
        reset.Binding.Should().Be(tuple);
        reset.ResolveDesired(new(tuple.Service, [tuple])).Should().Be(tuple);
        SessionProviderOverrides.Parse(reset.Serialize()).Serialize().Should().Be(reset.Serialize());
    }

    [Theory]
    [InlineData(SessionProviderField.Model)]
    [InlineData(SessionProviderField.Reasoning)]
    [InlineData(SessionProviderField.Context)]
    public void LastDependentResetRemovesBindingAndRestoresInheritance(SessionProviderField field)
    {
        var tuple = Tuple();
        var sparse = new SessionProviderOverrides(model: field == SessionProviderField.Model ? tuple.Model : null,
            reasoning: field == SessionProviderField.Reasoning ? tuple.Reasoning : null,
            context: field == SessionProviderField.Context ? tuple.Context : null, binding: tuple);
        sparse.ResolveDesired(new(ProviderService.Local, [tuple])).Should().Be(tuple);
        sparse.Reset(field).Serialize().Should().Be(SessionProviderOverrides.Inherit.Serialize());
    }

    [Theory]
    [InlineData("service")]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("missing")]
    [InlineData("unset")]
    public void BoundDependentOverridesNeverCarryAcrossProviderAccountRevisionOrModel(string change)
    {
        var tuple = Tuple();
        var policy = new SessionProviderOverrides(reasoning: tuple.Reasoning, binding: tuple);
        var app = change switch
        {
            "service" => new ProviderProfilePreferences(ProviderService.Claude, [Tuple(ProviderService.Claude)]),
            "account" => new(ProviderService.Local, [Tuple(account: "other")]),
            "revision" => new(ProviderService.Local, [Tuple(revision: 2)]),
            "model" => new(ProviderService.Local, [Tuple(model: "other")]),
            "missing" => new(ProviderService.Local, []),
            _ => new(ProviderService.Unset, [tuple]),
        };
        policy.ResolveDesired(app).Should().BeNull();
    }

    [Theory]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("binding")]
    [InlineData("service")]
    [InlineData("bound-service")]
    [InlineData("bound-model")]
    [InlineData("bound-reasoning")]
    [InlineData("bound-context")]
    public void InvalidSparseBindingsFailClosed(string change)
    {
        var tuple = Tuple();
        Action act = () => _ = change switch
        {
            "model" => new SessionProviderOverrides(model: tuple.Model),
            "reasoning" => new(reasoning: tuple.Reasoning),
            "context" => new(context: tuple.Context),
            "binding" => new(binding: tuple),
            "service" => new((ProviderService)99),
            "bound-service" => new(ProviderService.Claude, tuple.Model, binding: tuple),
            "bound-model" => new(model: new("other"), binding: tuple),
            "bound-reasoning" => new(reasoning: new("other"), binding: tuple),
            _ => new(context: new("other"), binding: tuple),
        };
        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Schema\":2}")]
    [InlineData("{\"Schema\":1,\"Service\":null,\"Model\":null,\"Reasoning\":null,\"Context\":null}")]
    [InlineData("{\"Schema\":1,\"Service\":99,\"Model\":null,\"Reasoning\":null,\"Context\":null,\"Binding\":null}")]
    public void MalformedPartialUnknownPolicyIsNotInheritance(string text) =>
        ((Action)(() => SessionProviderOverrides.Parse(text))).Should().Throw<InvalidDataException>();

    [Fact]
    public void CanonicalBytesBoundsUnknownPropertiesAndInvalidIdsFailClosed()
    {
        var text = SessionProviderOverrides.Inherit.Serialize();
        ((Action)(() => SessionProviderOverrides.Parse(text + " "))).Should().Throw<InvalidDataException>();
        ((Action)(() => SessionProviderOverrides.Parse(text.Replace("\"Schema\":1", "\"Schema\":1,\"Unknown\":0", StringComparison.Ordinal)))).Should().Throw<InvalidDataException>();
        ((Action)(() => SessionProviderOverrides.Parse(new string('x', SessionProviderOverrides.MaximumUtf8Bytes + 1)))).Should().Throw<InvalidDataException>();
        var tuple = Tuple();
        var bound = new SessionProviderOverrides(model: tuple.Model, binding: tuple).Serialize();
        ((Action)(() => SessionProviderOverrides.Parse(bound.Replace("\"model\"", "\"bad\\tvalue\"", StringComparison.Ordinal)))).Should().Throw<InvalidDataException>();
        ((Action)(() => SessionProviderOverrides.Parse(bound.Replace("\"CatalogueRevision\":{\"Value\":1}", "\"CatalogueRevision\":{\"Value\":0}", StringComparison.Ordinal)))).Should().Throw<InvalidDataException>();
        ((Action)(() => SessionProviderOverrides.Inherit.Reset((SessionProviderField)99))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FullyExplicitTupleCanResolveWithoutFabricatedAppTuple()
    {
        var tuple = Tuple();
        var policy = new SessionProviderOverrides(tuple.Service, tuple.Model, tuple.Reasoning, tuple.Context, tuple);
        policy.ResolveDesired(new(ProviderService.Unset, [])).Should().Be(tuple);
        policy.ResolveDesired(new(ProviderService.Local, [Tuple(account: "old", revision: 2)])).Should().Be(tuple);
        new SessionProviderOverrides(model: tuple.Model, context: tuple.Context, binding: tuple)
            .ResolveDesired(new(ProviderService.Local, [])).Should().BeNull();
        new SessionProviderOverrides(model: tuple.Model, reasoning: tuple.Reasoning, binding: tuple)
            .ResolveDesired(new(ProviderService.Local, [])).Should().BeNull();
        new SessionProviderOverrides(context: tuple.Context, binding: tuple).Reset(SessionProviderField.Service)
            .Context.Should().Be(tuple.Context);
    }
}
