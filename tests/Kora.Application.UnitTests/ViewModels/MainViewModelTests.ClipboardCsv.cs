using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NativeCsvBindingOnlyInspectsExactOriginalSnapshotWithoutAnySourceUseOrIo()
    {
        var fixture = new Fixture();
        fixture.ClipboardReader.Text = "=literal,\"private marker,a\"\nlast";
        await fixture.ViewModel.PreviewClipboardAsync();
        var source = fixture.ViewModel.ClipboardPreview!;
        var inspect = fixture.ViewModel.BindClipboardCsvInspection(source);
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var transcript = fixture.ViewModel.Transcript;
        inspect(TestContext.Current.CancellationToken).Inspection!.Should()
            .Be(new CommaSeparatedInspection(CommaSeparatedSyntax.Admitted, 2, 1, 2));
        fixture.ViewModel.ClipboardPreview.Should().BeSameAs(source);
        source.Text.Should().Be(fixture.ClipboardReader.Text);
        fixture.ClipboardReader.Calls.Should().Be(1);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.HostStore.Records.Should().BeEmpty();
        fixture.Audit.Events.Should().BeEmpty();
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.Transcript.Should().Be(transcript);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        inspect(cancellation.Token).Outcome.Should().Be(ClipboardCsvOutcome.Cancelled);
        fixture.ViewModel.ClearClipboardPreview();
        await fixture.ViewModel.PreviewClipboardAsync();
        inspect(TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardCsvOutcome.Stale);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("owner")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("dispose")]
    public async Task NativeCsvBindingRetainsOriginalHostRevisions(string change)
    {
        var fixture = new Fixture();
        var owner = 1L;
        fixture.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        await fixture.ViewModel.PreviewClipboardAsync();
        var inspect = fixture.ViewModel.BindClipboardCsvInspection(fixture.ViewModel.ClipboardPreview!);
        switch (change)
        {
            case "privacy":
                var prior = fixture.PrivacyObservation.Current;
                fixture.PrivacyObservation.Current = prior with { TopologyRevision = prior.TopologyRevision + 1 };
                fixture.PrivacyObservation.Publish(new(prior, fixture.PrivacyObservation.Current,
                    Kora.Core.Platform.WindowsPrivacyChangeReason.DeviceTopology));
                break;
            case "owner": owner++; break;
            case "call":
                await fixture.ViewModel.SetManualCallAsync(true, RequestOrigin.LocalUi, fixture.ViewModel.CallPolicyRevision,
                    TestContext.Current.CancellationToken);
                break;
            case "input": fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true); break;
            default: fixture.ViewModel.Dispose(); break;
        }
        inspect(TestContext.Current.CancellationToken).Outcome.Should().Be(ClipboardCsvOutcome.Stale);
        fixture.ViewModel.ClipboardPreview.Should().BeNull();
    }

    [Fact]
    public async Task InternalCsvFailureIsNotInvalidSyntaxOrAContentBearingGenericResponse()
    {
        var logger = new CsvLogger();
        var fixture = new Fixture(logger: logger);
        await fixture.ViewModel.PreviewClipboardAsync();
        var inspect = fixture.ViewModel.BindClipboardCsvInspection(fixture.ViewModel.ClipboardPreview!);
        fixture.ViewModel.BindClipboardOwnershipGate(() => throw new IOException("private marker"));
        inspect(TestContext.Current.CancellationToken).Should().Be(new ClipboardCsvResult(ClipboardCsvOutcome.Unavailable));
        fixture.ViewModel.ResponseBody.Should().NotContain("private marker");
        fixture.ViewModel.Transcript.Should().NotContain("private marker");
        logger.Messages.Should().Contain(message => message.Contains("Native comma-separated inspection failed internally", StringComparison.Ordinal))
            .And.NotContain(message => message.Contains("private marker", StringComparison.Ordinal));
    }

    private sealed class CsvLogger : ILogger<Kora.Application.ViewModels.MainViewModel>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
