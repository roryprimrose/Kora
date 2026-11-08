using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Presentation;

namespace Kora.Windows.IntegrationTests;

public sealed partial class DetailWindowControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewFinalMarkdownCapturesConfirmedModeButExactViewerReopenPreservesModeAndFind(bool source)
    {
        var calls = 0;
        using var f = new Fixture(checklistsAvailable: true, markdownDisplay: (_, _) =>
        {
            calls++;
            return Task.FromResult(source ? MarkdownDisplayMode.Source : MarkdownDisplayMode.Rendered);
        });
        var content = ChecklistContent();
        (await f.Controller.OpenCurrentResponseAsync(content, null, () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
        var state = f.Views.Single().State;
        state.IsSource.Should().Be(source);
        state.SetSource(!source);
        state.Search("Alpha").Should().BeTrue();
        var position = state.MatchStart;
        (await f.Controller.OpenCurrentResponseAsync(content, null, () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
        state.IsSource.Should().Be(!source);
        state.MatchStart.Should().Be(position);
        state.SearchQuery.Should().Be("Alpha");
        calls.Should().Be(1);
        (await f.Controller.OpenCurrentResponseAsync(ChecklistContent(), null, () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
        f.Views[1].State.IsSource.Should().Be(source);
        state.IsSource.Should().Be(!source);
        f.Clipboard.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("source")]
    [InlineData("dispose")]
    [InlineData("failure")]
    public async Task AwaitedPreferenceCaptureNeverPublishesAfterRevocationOrFailure(string boundary)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<MarkdownDisplayMode>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(markdownDisplay: async (_, _) => { entered.SetResult(); return await release.Task; });
        var admitted = true;
        var pending = f.Controller.OpenCurrentResponseAsync(ChecklistContent(), null, () => admitted, TestContext.Current.CancellationToken);
        await entered.Task;
        f.Views.Should().BeEmpty("no native source may be created before the preference is confirmed");
        if (boundary is "privacy") { f.Controller.ClearForPrivacy(); }
        if (boundary is "source") { admitted = false; }
        if (boundary is "dispose") { f.Controller.Dispose(); }
        var joined = f.Controller.JoinPreferenceReadsAsync();
        joined.IsCompleted.Should().BeFalse("cancellation is not physical preference-read quiescence");
        pending.IsCompleted.Should().BeFalse("revocation must not pretend that the pending read physically finished");
        if (boundary is "failure") { release.SetException(new InvalidDataException("Unconfirmed preference")); }
        else { release.SetResult(MarkdownDisplayMode.Source); }
        var failure = await new Func<Task>(async () => await pending).Should().ThrowAsync<Exception>();
        if (boundary is "failure") { failure.Which.Should().BeOfType<MarkdownDisplayConfigurationService.CaptureUnavailableException>(); }
        else { failure.Which.Should().NotBeOfType<MarkdownDisplayConfigurationService.CaptureUnavailableException>(); }
        await joined;
        f.Controller.JoinPreferenceReadsAsync().IsCompleted.Should().BeTrue();
        f.Views.Should().BeEmpty();
        f.Clipboard.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ExcludedReadersNeverReadOrApplyMarkdownDefault()
    {
        using var f = new Fixture(markdownDisplay: (_, _) => throw new InvalidOperationException("Excluded source must not read preferences."));
        f.Controller.OpenEmbeddedPage(f.Documents.GetStartPage(), null).Should().NotContain("unavailable");
        (await f.Controller.OpenCurrentResponseAsync(OrdinaryContent(), null, () => true, TestContext.Current.CancellationToken)).Should().BeTrue();
        var original = ConversationContent();
        await f.Controller.OpenConversationDetailAsync(original, null, () => true, _ => Task.FromResult(original), CancellationToken.None);
        f.Views.Should().OnlyContain(view => !view.State.IsSource);
    }
}
