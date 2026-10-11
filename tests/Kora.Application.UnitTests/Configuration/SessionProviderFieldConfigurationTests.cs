using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class SessionProviderFieldConfigurationTests
{
    [Theory]
    [InlineData(SessionProviderField.Model, "compatible-model", false, ProviderService.Local)]
    [InlineData(SessionProviderField.Reasoning, "exact-effort", false, ProviderService.Local)]
    [InlineData(SessionProviderField.Context, "exact-window", false, ProviderService.Local)]
    [InlineData(SessionProviderField.Reasoning, "exact-effort", true, ProviderService.Local)]
    [InlineData(SessionProviderField.Context, "exact-window", true, ProviderService.Local)]
    [InlineData(SessionProviderField.Model, "compatible-model", false, ProviderService.Claude)]
    [InlineData(SessionProviderField.Reasoning, "exact-effort", true, ProviderService.Claude)]
    [InlineData(SessionProviderField.Context, "exact-window", true, ProviderService.Claude)]
    public async Task OneFieldUsesEffectiveServiceAndPreservesEveryOtherSparseField(SessionProviderField field, string id,
        bool explicitOther, ProviderService service)
    {
        await using var f = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        f.Profiles.Catalogue.FieldCatalogue = true;
        var tuple = f.Profiles.Preferences.Value!.For(ProviderService.Local)! with { Service = service };
        f.Profiles.Preferences.Value = new(ProviderService.Codex, [tuple]);
        var previous = new SessionProviderOverrides(service,
            explicitOther ? tuple.Model : null,
            explicitOther && field != SessionProviderField.Reasoning ? tuple.Reasoning : null,
            explicitOther && field != SessionProviderField.Context ? tuple.Context : null,
            explicitOther ? tuple : null);
        f.Snapshot = f.Snapshot with { Desired = previous };
        var appBytes = f.Profiles.Preferences.Value.Serialize();
        var draft = await f.Inspect();
        draft.Effective.Should().Be(tuple);
        var proposal = f.Service.SelectField(draft, field, new(id));
        proposal.Field.Should().Be(field);
        proposal.PreviousEffective.Should().Be(tuple);
        proposal.AppRevision.Should().Be(0);
        proposal.ResultingEffective.Should().Be(proposal.Desired.ResolveDesired(draft.App));
        proposal.ResultingEffective!.Service.Should().Be(service);
        proposal.ResultingEffective.Model.Should().Be(field == SessionProviderField.Model ? new(id) : tuple.Model);
        proposal.ResultingEffective.Reasoning.Should().Be(field == SessionProviderField.Reasoning ? new(id) : tuple.Reasoning);
        proposal.ResultingEffective.Context.Should().Be(field == SessionProviderField.Context ? new(id) : tuple.Context);
        f.Snapshot.Desired.Serialize().Should().Be(previous.Serialize());
        var saved = await f.Service.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        saved.Desired.Service.Should().Be(previous.Service);
        saved.Desired.Model.Should().Be(field == SessionProviderField.Model ? new(id) : previous.Model);
        saved.Desired.Reasoning.Should().Be(field == SessionProviderField.Reasoning ? new(id) : previous.Reasoning);
        saved.Desired.Context.Should().Be(field == SessionProviderField.Context ? new(id) : previous.Context);
        saved.Desired.Serialize().Should().Be(proposal.Desired.Serialize());
        saved.Revision.Should().Be(1);
        f.Profiles.Preferences.Value.Serialize().Should().Be(appBytes);
        f.Profiles.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(SessionProviderField.Reasoning, "exact-effort", "exact-window")]
    [InlineData(SessionProviderField.Context, "exact-window", "exact-effort")]
    public async Task RetainedNullFieldsInheritLaterAppEditsRatherThanPreviewBaseline(SessionProviderField field, string id, string inherited)
    {
        await using var f = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        f.Profiles.Catalogue.FieldCatalogue = true;
        var draft = await f.Inspect();
        var proposal = f.Service.SelectField(draft, field, new(id));
        await f.Service.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token);
        var tuple = draft.Effective!;
        var changed = field == SessionProviderField.Reasoning ? tuple with { Context = new(inherited) } : tuple with { Reasoning = new(inherited) };
        f.Profiles.Preferences.Value = new(ProviderService.Local, [changed]);
        var next = await f.Inspect();
        next.Effective.Should().Be(field == SessionProviderField.Reasoning ? changed with { Reasoning = new(id) } : changed with { Context = new(id) });
        f.Service.Discard(next);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("unset")]
    [InlineData("missing-tuple")]
    [InlineData("held")]
    [InlineData("expired")]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("model")]
    [InlineData("reasoning")]
    [InlineData("context")]
    [InlineData("installation")]
    public async Task MissingOrUnresolvedEffectiveTupleRequiresCompleteProfileWithoutMutation(string failure)
    {
        await using var f = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        switch (failure)
        {
            case "unavailable": f.Profiles.Catalogue.Status = ProviderCatalogueStatus.Unavailable; break;
            case "unset": f.Profiles.Preferences.Value = new(ProviderService.Unset, []); break;
            case "missing-tuple": f.Profiles.Preferences.Value = new(ProviderService.Local, []); break;
            case "held": f.Snapshot = f.Snapshot with { Held = true }; break;
            case "account": f.Profiles.Catalogue.Account = "other"; break;
            case "revision": f.Profiles.Catalogue.Revision = 2; break;
            case "model": f.Profiles.Catalogue.Model = "retired"; break;
            case "reasoning": f.Profiles.Catalogue.Reasoning = "other"; break;
            case "context": f.Profiles.Catalogue.Context = "other"; break;
            case "installation": f.Profiles.Catalogue.Installation = LocalProviderInstallation.Unknown; break;
        }
        var draft = await f.Inspect();
        if (failure is "expired") { f.Profiles.Time.Now = f.Profiles.Catalogue.Expiry; }
        f.Service.Invoking(value => value.SelectField(draft, SessionProviderField.Reasoning, new("managed-effort")))
            .Should().Throw<InvalidOperationException>().WithMessage("*complete*profile*");
        f.Snapshot.Revision.Should().Be(0);
        f.Profiles.Preferences.Writes.Should().Be(0);
        f.Store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SessionProviderField.Model, "incompatible-model")]
    [InlineData(SessionProviderField.Model, "retired-model")]
    [InlineData(SessionProviderField.Reasoning, "other-effort")]
    [InlineData(SessionProviderField.Context, "other-window")]
    public async Task UnsupportedRetainedDependenciesOrRetiredChoiceRequireCompleteProfile(SessionProviderField field, string id)
    {
        await using var f = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        f.Profiles.Catalogue.FieldCatalogue = true;
        var draft = await f.Inspect();
        f.Service.Invoking(value => value.SelectField(draft, field, new(id))).Should().Throw<InvalidOperationException>();
        f.Snapshot.Revision.Should().Be(0);
    }

    [Theory]
    [InlineData(SessionProviderField.Service)]
    [InlineData((SessionProviderField)99)]
    public async Task NonTupleFieldsCannotEnterSingleFieldReview(SessionProviderField field)
    {
        await using var f = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        var draft = await f.Inspect();
        f.Service.Invoking(value => value.SelectField(draft, field, new("exact-model"))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public async Task EvenCompatibleModelCannotRebindPreviousExplicitDependentFields()
    {
        await using var f = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        f.Profiles.Catalogue.FieldCatalogue = true;
        f.Snapshot = f.Snapshot with { Desired = SessionProviderOverrides.Inherit.Select(f.Profiles.Preferences.Value!.For(ProviderService.Local)!, SessionProviderField.Reasoning) };
        var draft = await f.Inspect();
        f.Service.Invoking(value => value.SelectField(draft, SessionProviderField.Model, new("compatible-model")))
            .Should().Throw<InvalidDataException>().WithMessage("*complete tuple*");
        f.Snapshot.Revision.Should().Be(0);
    }

    [Fact]
    public async Task LegacyTripleFieldReviewCannotClaimSuccessWithIgnoredInheritedChoices()
    {
        await using var f = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        f.Profiles.Catalogue.FieldCatalogue = true;
        var draft = await f.Inspect();
        var model = draft.Catalogue!.Models[0];
        f.Service.Invoking(value => value.Select(draft, SessionProviderField.Model, model, model.Reasoning[1], model.Context[1]))
            .Should().Throw<InvalidOperationException>().WithMessage("*sparse result*");
        f.Snapshot.Revision.Should().Be(0);
    }

    [Fact]
    public async Task AppRevisionRefreshAndDiscardInvalidateSingleFieldConfirmationWithoutEviction()
    {
        await using var f = new SessionProviderProfileConfigurationTests.Fixture(current: true);
        var draft = await f.Inspect();
        var proposal = f.Service.SelectField(draft, SessionProviderField.Reasoning, new("managed-effort"));
        await f.Profiles.Refresh();
        await f.Service.Invoking(value => value.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        draft = await f.Inspect();
        proposal = f.Service.SelectField(draft, SessionProviderField.Context, new("managed-window"));
        f.Service.Discard(draft);
        await f.Service.Invoking(value => value.Confirm(proposal, RequestOrigin.LocalUi, () => f.Eligible, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Snapshot.Revision.Should().Be(0);
    }
}
