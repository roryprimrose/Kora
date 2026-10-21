using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Providers;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(ProviderProfileField.Model, "native")]
    [InlineData(ProviderProfileField.Reasoning, "native")]
    [InlineData(ProviderProfileField.Context, "native")]
    [InlineData(ProviderProfileField.Model, "typed")]
    [InlineData(ProviderProfileField.Reasoning, "typed")]
    [InlineData(ProviderProfileField.Context, "typed")]
    [InlineData(ProviderProfileField.Model, "activated")]
    [InlineData(ProviderProfileField.Reasoning, "activated")]
    [InlineData(ProviderProfileField.Context, "activated")]
    public async Task RealIndividualFieldRoutesReviewThenSaveTheSameExactCompleteTuple(ProviderProfileField field, string route)
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        var original = ProviderProfileFieldConfigurationTests.BaseTuple();
        f.ProfilePreferences.Value = new(ProviderService.Local, [original, ProviderProfileFieldConfigurationTests.BaseTuple(ProviderService.Claude)]);
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        var id = ProviderProfileFieldConfigurationTests.Replacement(field);
        var text = $"set providers.app.{field.ToString().ToLowerInvariant()} to {id} for account at 1";
        if (route is "native")
        {
            await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
            if (field == ProviderProfileField.Model)
            {
                f.ViewModel.SelectedProviderProfileModel = f.ViewModel.ProviderProfileModels.Single(model => string.Equals(model.Id.Value, id, StringComparison.Ordinal));
                f.ViewModel.CanReviewProviderModel.Should().BeTrue();
                await f.ViewModel.ReviewProviderModelCommand.ExecuteAsync();
            }
            if (field == ProviderProfileField.Reasoning)
            {
                f.ViewModel.SelectedProviderReasoningField = f.ViewModel.ProviderReasoningFieldChoices.Single(option => string.Equals(option.Id.Value, id, StringComparison.Ordinal));
                f.ViewModel.SelectedProviderReasoningField = f.ViewModel.SelectedProviderReasoningField;
                f.ViewModel.CanReviewProviderReasoning.Should().BeTrue();
                await f.ViewModel.ReviewProviderReasoningCommand.ExecuteAsync();
            }
            if (field == ProviderProfileField.Context)
            {
                f.ViewModel.SelectedProviderContextField = f.ViewModel.ProviderContextFieldChoices.Single(option => string.Equals(option.Id.Value, id, StringComparison.Ordinal));
                f.ViewModel.SelectedProviderContextField = f.ViewModel.SelectedProviderContextField;
                f.ViewModel.CanReviewProviderContext.Should().BeTrue();
                await f.ViewModel.ReviewProviderContextCommand.ExecuteAsync();
            }
        }
        if (route is "typed") { await f.RunAsync("Kora, " + text); }
        if (route is "activated") { await f.RaiseActivatedTranscriptAsync("Kora, " + text, 1); }
        f.ProfilePreferences.Writes.Should().Be(0);
        f.ViewModel.CanConfirmProviderField.Should().BeTrue();
        var preview = f.ViewModel.ProviderFieldPreviewStatus;
        preview.Should().Contain("Current:").And.Contain("Proposed:").And.Contain("Nothing is saved until you confirm")
            .And.Contain("Account: account").And.NotContain("preferenceRevision").And.Contain(id);
        var confirm = "confirm providers.app.profile " + f.ViewModel.ProviderFieldConfirmationId!.Value.ToString("D");
        if (route is "native") { await f.ViewModel.ConfirmProviderFieldCommand.ExecuteAsync(); }
        if (route is "typed") { await f.RunAsync(confirm); }
        if (route is "activated") { await f.RaiseActivatedTranscriptAsync("Kora, " + confirm, 1); }
        f.ViewModel.ResponseBody.Should().Contain("Desired configuration saved only");
        f.ProfilePreferences.Value!.For(ProviderService.Local).Should().Be(ProviderProfileRules.ReplaceField(original, field, new(id)));
        f.ProfilePreferences.Value.For(ProviderService.Claude).Should().Be(ProviderProfileFieldConfigurationTests.BaseTuple(ProviderService.Claude));
        f.ProfilePreferences.Value.Service.Should().Be(ProviderService.Local);
        f.ProfilePreferences.Pending.Should().BeFalse();
        f.ProfilePreferences.Writes.Should().Be(1);
        f.ViewModel.ProviderFieldConfirmationId.Should().BeNull();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ProviderPreferences.Mode.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealFieldCancellationDoesNotWriteAndCannotReplay(bool native)
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(ProviderService.Local, [ProviderProfileFieldConfigurationTests.BaseTuple()]);
        await f.ViewModel.InitializeAsync();
        if (native)
        {
            await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
            f.ViewModel.SelectedProviderContextField = f.ViewModel.ProviderContextFieldChoices[1];
            await f.ViewModel.ReviewProviderContextCommand.ExecuteAsync();
        }
        else { await f.RunAsync("set providers.app.context to exact-window for account at 1"); }
        var id = f.ViewModel.ProviderFieldConfirmationId!.Value;
        if (native) { await f.ViewModel.CancelProviderFieldCommand.ExecuteAsync(); }
        else { await f.RunAsync("cancel providers.app.profile " + id.ToString("D")); }
        f.ViewModel.ResponseBody.Should().Contain("Cancelled; no write");
        f.ProfilePreferences.Writes.Should().Be(0);
        await f.RunAsync("confirm providers.app.profile " + id.ToString("D"));
        f.ViewModel.ResponseTitle.Should().Be("App provider configuration not confirmed.");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("incompatible")]
    [InlineData("missing-base")]
    [InlineData("model-id")]
    [InlineData("account")]
    [InlineData("revision")]
    [InlineData("service")]
    [InlineData("service-return")]
    [InlineData("save")]
    [InlineData("call")]
    [InlineData("catalogue")]
    [InlineData("cancelled-confirm")]
    [InlineData("foreign-confirm")]
    [InlineData("missing-field-id")]
    public async Task RealFieldPathsRejectInvalidBasesAndConfirmationChangesWithoutFallback(string failure)
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(ProviderService.Local, failure is "missing-base" ? [] : [ProviderProfileFieldConfigurationTests.BaseTuple()]);
        await f.ViewModel.InitializeAsync();
        var id = failure is "incompatible" ? "incompatible-model" : failure is "model-id" ? "Unknown" : "compatible-model";
        if (failure is "missing-field-id")
        { await f.ViewModel.ExecuteProviderProfileCommandAsync(new(AppearanceCommandOperation.Set, Field: ProviderProfileField.Model),
            SecurityAuditInitiator.TypedCommand, cancellationToken: TestContext.Current.CancellationToken); }
        else { await f.RunAsync($"set providers.app.model to {id} for {(failure is "account" ? "other" : "account")} at {(failure is "revision" ? 2 : 1)}"); }
        if (failure is "service" or "service-return" or "save" or "call" or "catalogue" or "cancelled-confirm" or "foreign-confirm")
        {
            var confirmation = f.ViewModel.ProviderFieldConfirmationId!.Value;
            if (failure is "foreign-confirm") { confirmation = Guid.NewGuid(); }
            if (failure is "service") { f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Claude); }
            if (failure is "service-return")
            {
                var original = f.ViewModel.SelectedProviderServiceChoice;
                f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Claude);
                f.ViewModel.SelectedProviderServiceChoice = original;
            }
            if (failure is "save") { f.ProfilePreferences.Value = new(ProviderService.Unset, [ProviderProfileFieldConfigurationTests.BaseTuple()]); }
            if (failure is "call") { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
            if (failure is "catalogue") { f.ProfileCatalogue.Revision++; }
            if (failure is "cancelled-confirm")
            {
                using var cancellation = new CancellationTokenSource();
                await cancellation.CancelAsync();
                await f.ViewModel.ExecuteProviderProfileCommandAsync(new(AppearanceCommandOperation.Set, Confirmation: confirmation),
                    SecurityAuditInitiator.TypedCommand, cancellationToken: cancellation.Token);
            }
            else { await f.RunAsync("confirm providers.app.profile " + confirmation.ToString("D")); }
        }
        f.ProfilePreferences.Writes.Should().Be(0);
        f.ViewModel.ProviderFieldConfirmationId.Should().BeNull();
        f.ViewModel.ResponseTitle.Should().Be("App provider configuration not confirmed.");
        if (failure is "incompatible" or "missing-base") { f.ViewModel.ResponseBody.Should().Contain("complete providers.app.profile draft/save"); }
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NativeFieldPickersRejectForeignSameLabelReferencesAndPreserveOtherServiceDefault()
    {
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        f.ProfileCatalogue.Status = ProviderCatalogueStatus.Current;
        f.ProfileCatalogue.FieldCatalogue = true;
        f.ProfilePreferences.Value = new(ProviderService.Unset, [ProviderProfileFieldConfigurationTests.BaseTuple(ProviderService.Claude)]);
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Claude);
        var model = f.ViewModel.ProviderProfileModels[0];
        f.ViewModel.SelectedProviderProfileModel = new(model.Id, model.Label, model.Selection, model.Reasoning, model.Context);
        f.ViewModel.CanReviewProviderModel.Should().BeFalse();
        f.ViewModel.SelectedProviderReasoningField = f.ViewModel.ProviderReasoningFieldChoices[1] with { };
        f.ViewModel.CanReviewProviderReasoning.Should().BeFalse();
        f.ViewModel.SelectedProviderContextField = f.ViewModel.ProviderContextFieldChoices[1] with { };
        f.ViewModel.CanReviewProviderContext.Should().BeFalse();
        await f.ViewModel.Invoking(viewModel => viewModel.ReviewProviderReasoningCommand.ExecuteAsync()).Should().ThrowAsync<InvalidOperationException>();
        await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
        f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Claude);
        f.ViewModel.SelectedProviderContextField = f.ViewModel.ProviderContextFieldChoices[1];
        await f.ViewModel.ReviewProviderContextCommand.ExecuteAsync();
        await f.ViewModel.ConfirmProviderFieldCommand.ExecuteAsync();
        f.ProfilePreferences.Value!.Service.Should().Be(ProviderService.Unset);
        f.ProfilePreferences.Value.For(ProviderService.Claude)!.Context.Value.Should().Be("exact-window");
    }

    [Fact]
    public async Task UnavailableOrMissingCompositionOffersNoFieldDefaults()
    {
        var missing = new Fixture();
        missing.ViewModel.ProviderReasoningFieldChoices.Should().BeEmpty();
        missing.ViewModel.ProviderContextFieldChoices.Should().BeEmpty();
        missing.ViewModel.CanReviewProviderModel.Should().BeFalse();
        missing.ViewModel.CanReviewProviderReasoning.Should().BeFalse();
        missing.ViewModel.CanReviewProviderContext.Should().BeFalse();
        missing.ViewModel.CanConfirmProviderField.Should().BeFalse();
        missing.ViewModel.ProviderFieldPreviewStatus.Should().Contain("No change awaiting confirmation");
        missing.ViewModel.SelectedProviderServiceChoice = new(new(ProviderService.Local, []), 1, Guid.NewGuid(),
            new(new(Guid.NewGuid()), new(1), true), Kora.Core.Hosting.RequestOrigin.LocalUi, static () => true);
        missing.ViewModel.ProviderReasoningFieldChoices.Should().BeEmpty();
        var f = new Fixture(enableProviderProfiles: true);
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("set providers.app.reasoning to managed-effort for account at 1");
        f.ProfilePreferences.Writes.Should().Be(0);
        f.ViewModel.CanReviewProviderModel.Should().BeFalse();
        f.ViewModel.CanConfirmProviderField.Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
