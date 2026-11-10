using AwesomeAssertions;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.ViewModels;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("typed")]
    [InlineData("voice")]
    public async Task MarkdownNativeTypedAndActivatedControlsShareReviewCancelSaveAndReset(string route)
    {
        await using var settings = new MarkdownDisplayConfigurationServiceTests.Fixture();
        var f = new Fixture(markdownDisplayConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        main.BindMarkdownDisplayNativeLifetime(static () => true);
        Task Run(string phrase, Func<Task> native) => route switch
        {
            "native" => native(),
            "voice" => f.RaiseActivatedTranscriptAsync("Kora, " + phrase, 1),
            _ => f.RunAsync(phrase),
        };
        await Run("get responses.markdown-display", main.InspectMarkdownDisplayCommand.ExecuteAsync);
        main.MarkdownDisplayStatus.Should().Contain("Rendered (unsaved default)");
        await Run("set responses.markdown-display to Source", main.SourceMarkdownDisplayCommand.ExecuteAsync);
        main.MarkdownDisplayReviewText.Should().Contain("Rendered (unsaved default) -> Source");
        settings.Preferences.Writes.Should().Be(0);
        await Run("cancel markdown display change", main.CancelMarkdownDisplayCommand.ExecuteAsync);
        await Run("set responses.markdown-display to Source", main.SourceMarkdownDisplayCommand.ExecuteAsync);
        await Run("confirm markdown display change", main.ConfirmMarkdownDisplayCommand.ExecuteAsync);
        settings.Preferences.Value.Should().Be(MarkdownDisplayMode.Source);
        settings.Preferences.Writes.Should().Be(1);
        await Run("reset responses.markdown-display", main.ResetMarkdownDisplayCommand.ExecuteAsync);
        settings.Preferences.Value.Should().Be(MarkdownDisplayMode.Source);
        await Run("confirm markdown display change", main.ConfirmMarkdownDisplayCommand.ExecuteAsync);
        settings.Preferences.Value.Should().Be(MarkdownDisplayMode.Rendered);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpeakCalls.Should().Be(0);
        f.Voice.StartCalls.Should().Be(route is "voice" ? 7 : 0,
            "only the seven explicitly supplied activated inputs may start captures; preference persistence starts none");
    }

    [Fact]
    public async Task MarkdownUncomposedNativeHiddenAndCorruptStateNeverAssumeRendered()
    {
        var unavailable = new Fixture();
        using var main = unavailable.ViewModel;
        main.MarkdownDisplayStatus.Should().Contain("not composed");
        main.CanControlMarkdownDisplay.Should().BeFalse();
        await main.InspectMarkdownDisplayCommand.ExecuteAsync();
        main.Transcript.Should().Contain("unavailable");
        await using var settings = new MarkdownDisplayConfigurationServiceTests.Fixture();
        settings.Preferences.LoadFailure = new InvalidDataException();
        var f = new Fixture(markdownDisplayConfiguration: settings.Service);
        using var corrupt = f.ViewModel;
        await corrupt.InitializeAsync();
        corrupt.MarkdownDisplayStatus.Should().Contain("unavailable");
        await corrupt.InspectMarkdownDisplayCommand.ExecuteAsync();
        corrupt.Transcript.Should().Contain("unavailable");
        corrupt.BindMarkdownDisplayNativeLifetime(static () => true);
        await corrupt.InspectMarkdownDisplayCommand.ExecuteAsync();
        corrupt.Transcript.Should().Contain("not confirmed");
        settings.Preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task MarkdownInvalidMissingReviewRepeatedReviewAndNativeRebindDoNotSave()
    {
        await using var settings = new MarkdownDisplayConfigurationServiceTests.Fixture();
        var f = new Fixture(markdownDisplayConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        await f.RunAsync("set responses.markdown-display to Source now");
        main.Transcript.Should().Contain("input not accepted");
        await f.RunAsync("confirm markdown display change");
        main.Transcript.Should().Contain("not confirmed");
        await f.RunAsync("review markdown display change");
        main.Transcript.Should().Contain("Choose Rendered or Source first");
        await f.RunAsync("set responses.markdown-display to Source");
        await f.RunAsync("review markdown display change");
        main.MarkdownDisplayReviewText.Should().Contain("-> Source");
        main.BindMarkdownDisplayNativeLifetime(static () => true);
        main.MarkdownDisplayReviewText.Should().Contain("No Markdown display");
        await main.RenderedMarkdownDisplayCommand.ExecuteAsync();
        await main.CancelMarkdownDisplayCommand.ExecuteAsync();
        settings.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkdownRequiresCurrentNameActivationAndAvailableName(bool corruptName)
    {
        await using var settings = new MarkdownDisplayConfigurationServiceTests.Fixture();
        var f = new Fixture(markdownDisplayConfiguration: settings.Service);
        using var main = f.ViewModel;
        if (corruptName) { f.NamePreferences.Name = "\0"; }
        await main.InitializeAsync();
        if (corruptName) { await f.RunAsync("set responses.markdown-display to Source"); }
        else { await f.RaiseActivatedTranscriptAsync("set responses.markdown-display to Source", 1); }
        main.Transcript.Should().Contain("current assistant-name voice prefix");
        settings.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkdownVoiceRequestedAuditCallChangeDeniesSaveWithoutSuccess()
    {
        await using var settings = new MarkdownDisplayConfigurationServiceTests.Fixture();
        var f = new Fixture(markdownDisplayConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, set responses.markdown-display to Source", 1);
        settings.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == SecurityAuditOutcome.Requested) { f.CallState.SetState(CallState.Active); }
        };
        await f.RaiseActivatedTranscriptAsync("Kora, confirm markdown display change", 1);
        settings.Preferences.Writes.Should().Be(0);
        main.Transcript.Should().Contain("not confirmed");
    }

    [Fact]
    public async Task MarkdownPrivateDispatcherRejectsModelInitiationWithoutAnyEffect()
    {
        await using var settings = new MarkdownDisplayConfigurationServiceTests.Fixture();
        var f = new Fixture(markdownDisplayConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        var execute = typeof(MainViewModel).GetMethod("ExecuteMarkdownDisplay",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await ((Task)execute.Invoke(main,
            [new Kora.Application.Configuration.MarkdownDisplayCommand(
                Kora.Application.Configuration.MarkdownDisplayCommand.OperationType.Review, MarkdownDisplayMode.Source),
                SecurityAuditInitiator.ModelSuggestion, null])!);
        main.Transcript.Should().Contain("unavailable");
        settings.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartCalls.Should().Be(0);
    }

    [Fact]
    public async Task MarkdownObservationCannotDiscloseAfterPublicationRevokesHost()
    {
        await using var settings = new MarkdownDisplayConfigurationServiceTests.Fixture();
        var f = new Fixture(markdownDisplayConfiguration: settings.Service);
        using var main = f.ViewModel;
        await main.InitializeAsync();
        settings.Preferences.Value = MarkdownDisplayMode.Source;
        settings.Service.Changed += (_, _) => main.BindCallOwnershipGate(static () => false);
        await f.RunAsync("get responses.markdown-display");
        main.Transcript.Should().Contain("not confirmed").And.Contain("host admission changed before presentation");
        settings.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
