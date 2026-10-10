using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Network;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NativeTypedAndVoiceUriControlsShareTheSameActions()
    {
        var (f, configuration) = CreateUriFixture();
        await using var admission = f.OutputAdmission;
        f.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        f.Voice.DefaultMicrophoneId = "voice-fixture";
        await f.ViewModel.InitializeAsync();
        f.ViewModel.PreapprovedUriStatus.Should().Contain("No addresses");
        f.ViewModel.PreapprovedUriPatterns.Should().BeEmpty();
        f.ViewModel.AddPreapprovedUriCommand.CanExecute(null).Should().BeFalse();
        f.ViewModel.RemovePreapprovedUriCommand.CanExecute(null).Should().BeFalse();
        f.ViewModel.ClearPreapprovedUrisCommand.CanExecute(null).Should().BeFalse();
        f.ViewModel.PreapprovedUriInput = "https://example.com/*";
        f.ViewModel.PreapprovedUriInput = "https://example.com/*";
        await f.ViewModel.AddPreapprovedUriCommand.ExecuteAsync();
        f.ViewModel.PreapprovedUriInput.Should().BeEmpty();
        f.ViewModel.PreapprovedUriStatus.Should().Contain("1 address");
        f.ViewModel.SelectedPreapprovedUri = "https://example.com/*";
        f.ViewModel.SelectedPreapprovedUri = "https://example.com/*";
        await f.ViewModel.RefreshPreapprovedUrisCommand.ExecuteAsync();
        f.ViewModel.SelectedPreapprovedUri.Should().Be("https://example.com/*");
        await f.ViewModel.RemovePreapprovedUriCommand.ExecuteAsync();
        f.ViewModel.SelectedPreapprovedUri.Should().BeNull();
        await f.RunAsync("add preapproved address https://other.example/*");
        await f.RunAsync("list preapproved addresses");
        f.ViewModel.ResponseBody.Should().Contain("https://other.example/*");
        await f.ViewModel.ClearPreapprovedUrisCommand.ExecuteAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, add preapproved address https://voice.example/*", 1);
        configuration.LastInitiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
        configuration.Settings.Patterns.Should().Equal("https://voice.example/*");
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("list")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("clear")]
    [InlineData("locked")]
    [InlineData("owner")]
    [InlineData("disposed")]
    [InlineData("active")]
    [InlineData("question")]
    public async Task UriControlsRequireEveryActionAndALiveEligibleHost(string blocker)
    {
        var (f, configuration) = CreateUriFixture(blocker);
        await using var admission = f.OutputAdmission;
        if (blocker is "question")
        {
            f.Probe.Status = new("local.inference", "Local model", DependencyReadiness.Ready, "Ready");
            f.Reasoner.Question = new("Which target?", ["First", "Second"]);
        }
        await f.ViewModel.InitializeAsync();
        if (blocker is "locked") { f.Session.IsUnlocked = false; }
        if (blocker is "owner") { f.ViewModel.BindCallOwnershipGate(static () => false); }
        if (blocker is "disposed") { f.ViewModel.Dispose(); }
        if (blocker is "question")
        {
            await f.RunAsync("help me with this computer");
            await f.ViewModel.ActiveReasoningTask!;
            f.ViewModel.IsModelQuestionPending.Should().BeTrue();
        }
        if (blocker is "active")
        {
            Task? nested = null;
            configuration.BeforeExecute = () =>
            {
                f.ViewModel.CanChangePreapprovedUris.Should().BeFalse();
                nested = f.ViewModel.ExecutePreapprovedUriCommandAsync(
                    new(PreapprovedUriCommandOperation.Clear), SecurityAuditInitiator.LocalUser);
            };
            await f.ViewModel.RefreshPreapprovedUrisCommand.ExecuteAsync();
            await nested!;
            configuration.BeforeExecute = null;
            f.ViewModel.CanChangePreapprovedUris.Should().BeTrue();
            return;
        }
        f.ViewModel.CanChangePreapprovedUris.Should().BeFalse();
        await f.ViewModel.ExecutePreapprovedUriCommandAsync(
            new(PreapprovedUriCommandOperation.Clear), SecurityAuditInitiator.LocalUser);
        if (blocker is not "disposed") { f.ViewModel.Transcript.Should().Contain("requires the current owning unlocked host"); }
        if (blocker is "list") { f.ViewModel.PreapprovedUriStatus.Should().Contain("unavailable"); }
        configuration.Mutations.Should().Be(0);
    }

    [Theory]
    [InlineData("clarify")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("data")]
    [InlineData("operation")]
    [InlineData("argument")]
    [InlineData("enum")]
    [InlineData("unexpected")]
    public async Task UriControlsSurfaceFailuresAndReleaseTheActiveGate(string failure)
    {
        var (f, configuration) = CreateUriFixture();
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        configuration.Failure = failure switch
        {
            "io" => new IOException("fixture"),
            "access" => new UnauthorizedAccessException("fixture"),
            "data" => new InvalidDataException("fixture"),
            "operation" => new InvalidOperationException("fixture"),
            "argument" => new ArgumentException("fixture"),
            "unexpected" => new NotSupportedException("fixture"),
            _ => null,
        };
        var command = failure switch
        {
            "clarify" => new PreapprovedUriCommand(PreapprovedUriCommandOperation.Clarify, Error: "Exact syntax required."),
            "enum" => new PreapprovedUriCommand((PreapprovedUriCommandOperation)999),
            _ => new PreapprovedUriCommand(PreapprovedUriCommandOperation.List),
        };
        if (failure is "unexpected")
        {
            await f.ViewModel.Awaiting(model => model.ExecutePreapprovedUriCommandAsync(command, SecurityAuditInitiator.LocalUser))
                .Should().ThrowAsync<NotSupportedException>();
        }
        else
        {
            await f.ViewModel.ExecutePreapprovedUriCommandAsync(command, SecurityAuditInitiator.LocalUser);
            f.ViewModel.State.Should().Be(AssistantState.Failure);
            f.ViewModel.ResponseTitle.Should().Be(failure is "clarify"
                ? "Clarify the preapproved address setting." : "Preapproved addresses were not changed.");
        }
        f.ViewModel.CanChangePreapprovedUris.Should().BeTrue();
        configuration.Mutations.Should().Be(0);
    }

    private static (Fixture Fixture, UriUiConfiguration Configuration) CreateUriFixture(string? omitted = null)
    {
        var configuration = new UriUiConfiguration();
        return (new Fixture(
            preapprovedUriList: omitted is "list" ? null : new(configuration),
            preapprovedUriAdd: omitted is "add" ? null : new(configuration),
            preapprovedUriRemove: omitted is "remove" ? null : new(configuration),
            preapprovedUriClear: omitted is "clear" ? null : new(configuration)), configuration);
    }

    private sealed class UriUiConfiguration : IPreapprovedUriConfiguration
    {
        public PreapprovedUriSettings Settings { get; private set; } = PreapprovedUriSettings.Empty;
        public SecurityAuditInitiator LastInitiator { get; private set; }
        public int Mutations { get; private set; }
        public Exception? Failure { get; set; }
        public Action? BeforeExecute { get; set; }

        public PreapprovedUriSettings GetSettings()
        {
            BeforeExecute?.Invoke();
            if (Failure is { } exception) { throw exception; }
            return Settings;
        }

        public bool IsPreapproved(Uri uri) => GetSettings().IsPreapproved(uri);
        public PreapprovedUriSettings Add(string pattern, SecurityAuditInitiator initiator) =>
            Change(PreapprovedUriSettings.Create(Settings.Patterns.Append(pattern)), initiator);
        public PreapprovedUriSettings Remove(string pattern, SecurityAuditInitiator initiator) =>
            Change(PreapprovedUriSettings.Create(Settings.Patterns.Where(item =>
                !string.Equals(item, pattern, StringComparison.Ordinal))), initiator);
        public PreapprovedUriSettings Clear(SecurityAuditInitiator initiator) =>
            Change(PreapprovedUriSettings.Empty, initiator);

        private PreapprovedUriSettings Change(PreapprovedUriSettings next, SecurityAuditInitiator initiator)
        {
            GetSettings();
            Mutations++;
            LastInitiator = initiator;
            Settings = next;
            return next;
        }
    }
}
