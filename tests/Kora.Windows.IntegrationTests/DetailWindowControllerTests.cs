using Avalonia.Controls;

using AwesomeAssertions;

using Kora.Application.Documentation;
using Kora.Application.Presentation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class DetailWindowControllerTests
{
    [Fact]
    public void SameRevisionActivatesExistingViewerWithoutRerenderOrNewWindow()
    {
        using var fixture = new Fixture();
        var page = fixture.Documents.GetStartPage();
        fixture.Controller.OpenEmbeddedPage(page, null);
        fixture.Controller.OpenEmbeddedPage(page, null);
        fixture.Views.Should().ContainSingle();
        fixture.Views[0].Activations.Should().Be(2);
        fixture.Views[0].State.Content!.Source.Should().Be(page.Markdown);
        fixture.Views[0].State.Content!.SessionSource.Should().BeNull();
    }

    [Fact]
    public void ChangingEmbeddedRevisionOpensSeparatelyWithoutUpdatingExistingViewer()
    {
        using var fixture = new Fixture();
        var original = fixture.Documents.GetStartPage();
        fixture.Controller.OpenEmbeddedPage(original, null);
        var first = fixture.Views[0].State.Content!;
        fixture.Documents.Pages[0] = original with { Markdown = "Second immutable revision" };
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null);
        fixture.Views.Should().HaveCount(2);
        var second = fixture.Views[1].State.Content!;
        second.Reference.ItemId.Should().Be(first.Reference.ItemId);
        second.Reference.Revision.Should().Be(first.Reference.Revision + 1);
        fixture.Views[0].State.Content!.Source.Should().Be(original.Markdown);
        fixture.Views[1].State.Content!.Source.Should().Be("Second immutable revision");
        fixture.Controller.OpenEmbeddedPage(original, null).Should().Contain("not the current");
    }

    [Fact]
    public void EmbeddedTitleChangesAlsoReceiveSeparateImmutableRevision()
    {
        using var fixture = new Fixture();
        var original = fixture.Documents.GetStartPage();
        fixture.Controller.OpenEmbeddedPage(original, null);
        fixture.Documents.Pages[0] = original with { Title = "New native title" };
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null);
        fixture.Views.Should().HaveCount(2);
        fixture.Views[1].State.Reference.Revision.Should().Be(2);
        fixture.Views[0].State.Content!.Title.Should().Be(original.Title);
    }

    [Fact]
    public void OpenRejectsUntrustedPagePrivacyClosureAndNinthViewer()
    {
        using var fixture = new Fixture();
        fixture.Controller.OpenEmbeddedPage(new("untrusted", "Injected", "source"), null).Should().Contain("not the current");
        fixture.Accessible = false;
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null).Should().Contain("privacy");
        fixture.Views.Should().BeEmpty();
        fixture.Accessible = true;
        for (var index = 1; index < 9; index++)
        {
            fixture.Documents.Pages.Add(new("page" + index, "Page", "source"));
        }
        foreach (var page in fixture.Documents.Pages.Take(8)) { fixture.Controller.OpenEmbeddedPage(page, null); }
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.Pages[8], null).Should().Contain("close a viewer");
        fixture.Views.Should().HaveCount(8);
    }

    [Fact]
    public async Task PrivacyClearsEveryStateAndWindowIncludingSelectionsAndBlocksStaleCopy()
    {
        using var fixture = new Fixture();
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null);
        var view = fixture.Views[0];
        var reference = view.State.Reference;
        var generation = view.State.Generation;
        view.State.Search("immutable");
        view.State.MatchStart.Should().BeGreaterThanOrEqualTo(0);
        fixture.Controller.ClearForPrivacy();
        view.Cleared.Should().BeTrue();
        view.State.Content.Should().BeNull();
        view.State.ActiveText.Should().BeEmpty();
        view.State.MatchLength.Should().Be(0);
        view.State.SearchQuery.Should().BeEmpty();
        view.State.RenderState.Should().Be(DetailRenderState.Closed);
        await fixture.Controller.CopyAsync(reference, generation, confirmed: true);
        fixture.Clipboard.Writes.Should().BeEmpty();
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null);
        fixture.Views.Should().HaveCount(2);
        fixture.Views[1].State.Reference.ItemId.Should().NotBe(reference.ItemId);
    }

    [Fact]
    public async Task CopyRechecksAccessExactLiveGenerationAndOnlyReportsActualPlatformSuccess()
    {
        using var fixture = new Fixture();
        var page = fixture.Documents.GetStartPage();
        fixture.Controller.OpenEmbeddedPage(page, null);
        var state = fixture.Views[0].State;
        await fixture.Controller.CopyAsync(state.Reference, state.Generation + 1, confirmed: true);
        fixture.Clipboard.Writes.Should().BeEmpty();
        fixture.Accessible = false;
        await fixture.Controller.CopyAsync(state.Reference, state.Generation, confirmed: true);
        fixture.Clipboard.Writes.Should().BeEmpty();
        state.Status.Should().Contain("blocked");
        fixture.Accessible = true;
        await fixture.Controller.CopyAsync(state.Reference, state.Generation, confirmed: false);
        fixture.Clipboard.Writes.Should().ContainSingle().Which.Should().Be(page.Markdown);
        state.Status.Should().Contain("copied as Unicode plain text");
        fixture.Clipboard.Fail = true;
        await fixture.Controller.CopyAsync(state.Reference, state.Generation, confirmed: false);
        state.Status.Should().Contain("failed").And.NotContain("copied");
    }

    [Fact]
    public async Task CopyRevalidatesPrivacyImmediatelyBeforeWriterWithoutPlatformEffects()
    {
        using var fixture = new Fixture();
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null);
        var state = fixture.Views[0].State;
        var checks = 0;
        fixture.AccessGate = () => ++checks < 2;
        await fixture.Controller.CopyAsync(state.Reference, state.Generation, confirmed: true);
        fixture.Clipboard.Writes.Should().BeEmpty();
        state.Status.Should().Contain("privacy changed");
    }

    [Fact]
    public async Task SelectionCopyNeverSubstitutesTheWholeSourceAndUsesTheSamePrivacyGate()
    {
        using var fixture = new Fixture();
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null);
        var state = fixture.Views[0].State;
        state.SetSource(true);
        await fixture.Controller.CopyAsync(state.Reference, state.Generation, false, 2, 5);
        fixture.Clipboard.Writes.Should().ContainSingle().Which.Should().Be("Guide");
        state.Status.Should().Contain("Selected text");
        await fixture.Controller.CopyAsync(state.Reference, state.Generation, false, 0, 0);
        fixture.Clipboard.Writes.Should().ContainSingle();
        fixture.Accessible = false;
        await fixture.Controller.CopyAsync(state.Reference, state.Generation, true, 0, 1);
        fixture.Clipboard.Writes.Should().ContainSingle();
        fixture.Accessible = true;
        await fixture.Controller.CopyAsync(state.Reference, state.Generation + 1, true, 0, 1);
        state.Status.Should().Contain("stale");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidEmbeddedSourceIsVisibleRejectionNotAWindowOrUnhandledFailure(bool invalidUnicode)
    {
        var source = invalidUnicode ? new string('\ud800', 1) : " ";
        using var fixture = new Fixture();
        fixture.Documents.Pages[0] = fixture.Documents.GetStartPage() with { Markdown = source };
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null)
            .Should().Contain("invalid");
        fixture.Views.Should().BeEmpty();
    }

    [Fact]
    public async Task WindowCloseAndControllerDisposeForgetPresentationWithoutModifyingSource()
    {
        using var fixture = new Fixture();
        var page = fixture.Documents.GetStartPage();
        fixture.Controller.OpenEmbeddedPage(page, null);
        var view = fixture.Views[0];
        var generation = view.State.Generation;
        view.ClearAndClose();
        view.State.Content.Should().BeNull();
        page.Markdown.Should().Be("# Guide\n\nAn immutable source 😀 é.\r\n");
        await fixture.Controller.CopyAsync(view.State.Reference, generation, confirmed: true);
        fixture.Clipboard.Writes.Should().BeEmpty();
        fixture.Controller.OpenEmbeddedPage(page, null);
        fixture.Controller.Dispose();
        fixture.Views.Should().OnlyContain(item => item.Cleared);
        fixture.Controller.OpenEmbeddedPage(page, null).Should().Contain("privacy/input");
    }

    [Fact]
    public void MissingRendererShowsLabelledExactSourceWithoutPlatform()
    {
        using var fixture = new Fixture(rendererAvailable: false);
        fixture.Controller.OpenEmbeddedPage(fixture.Documents.GetStartPage(), null).Should().Contain("Renderer unavailable");
        fixture.Views[0].State.RenderState.Should().Be(DetailRenderState.SourceFallback);
        fixture.Views[0].State.ActiveText.Should().Be(fixture.Documents.GetStartPage().Markdown);
    }

    [Fact]
    public void NativeDisclosureGateRequiresAdditionalConfirmationAndExactUnicodeSource()
    {
        const string source = "private exact Unicode 😀 é\r\n";
        var content = new AdmittedDetailContent(new(new HostId<EvidenceIdentity>(Guid.NewGuid()), 1),
            DetailContentKind.PlainText, DetailContentOrigin.EmbeddedDocument,
            DetailSensitivity.DisclosureConfirmationRequired, "Private", "Host-admitted native test snapshot", source);
        var state = new DetailViewerState(content);
        state.TryGetCopySource(canAccess: false, disclosureConfirmed: true, out _).Should().BeFalse();
        state.TryGetCopySource(canAccess: true, disclosureConfirmed: false, out _).Should().BeFalse();
        state.Status.Should().Contain("Confirm disclosure");
        state.TryGetCopySource(canAccess: true, disclosureConfirmed: true, out var copied).Should().BeTrue();
        copied.Should().Be(source);
        state.Close();
        state.TryGetCopySource(canAccess: true, disclosureConfirmed: true, out _).Should().BeFalse();
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(bool rendererAvailable = true)
        {
            Controller = new(Documents, () => AccessGate?.Invoke() ?? Accessible,
                new(NullLogger<NativeDetailRenderer>.Instance, rendererAvailable), Clipboard,
                (state, _, _) =>
                {
                    var view = new FakeView(state);
                    Views.Add(view);
                    return view;
                }, NullLogger<DetailWindowController>.Instance);
        }
        public FakeDocumentation Documents { get; } = new();
        public FakeClipboard Clipboard { get; } = new();
        public List<FakeView> Views { get; } = [];
        public bool Accessible { get; set; } = true;
        public Func<bool>? AccessGate { get; set; }
        public DetailWindowController Controller { get; }
        public void Dispose() => Controller.Dispose();
    }

    private sealed class FakeDocumentation : IUserDocumentationProvider
    {
        public List<UserDocumentationPage> Pages { get; } = [new("guide", "Guide", "# Guide\n\nAn immutable source 😀 é.\r\n")];
        public IReadOnlyList<UserDocumentationPage> GetPages() => Pages;
        public UserDocumentationPage GetStartPage() => Pages[0];
    }

    private sealed class FakeView(DetailViewerState state) : IDetailView
    {
        public DetailViewerState State { get; } = state;
        public bool Cleared { get; private set; }
        public int Activations { get; private set; }
        public event EventHandler? Closed;
        public void ShowOwned(Window? owner) { }
        public void Activate() => Activations++;
        public void ClearAndClose()
        {
            if (Cleared) { return; }
            Cleared = true;
            State.Close();
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeClipboard : IDetailClipboard
    {
        public List<string> Writes { get; } = [];
        public bool Fail { get; set; }
        public Task WritePlainTextAsync(IDetailView view, string source)
        {
            if (Fail) { throw new InvalidOperationException("fake platform failure"); }
            Writes.Add(source);
            return Task.CompletedTask;
        }
    }
}
