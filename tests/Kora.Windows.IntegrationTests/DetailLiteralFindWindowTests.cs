using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class DetailLiteralFindWindowTests
{
    [Fact]
    public Task KeyboardFindAndProjectionChangesPreserveNativeFocusAndEscapeClosure() => HeadlessSession.RunAsync(() =>
    {
        var content = Content();
        var state = new DetailViewerState(content);
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source, content.Kind);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback);
        var window = new DetailWindow(state, result, (_, _, _) => throw new InvalidOperationException("No clipboard authority."));
        window.Show();
        try
        {
            var query = Required<TextBox>(window, "SearchInput");
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control });
            query.IsFocused.Should().BeTrue();
            query.Text = "alpha";
            query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            state.MatchStart.Should().Be(6);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3 });
            state.MatchStart.Should().Be(12);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F3, KeyModifiers = KeyModifiers.Shift });
            state.MatchStart.Should().Be(6);
            Required<CheckBox>(window, "SourceToggle").IsChecked = true;
            query.Text.Should().BeEmpty();
            state.CanNavigateFind.Should().BeFalse();
            Required<NamedTextBlock>(window, "FindSpan").IsVisible.Should().BeFalse();
            query.Text = "USER";
            Click(window, "FirstMatch");
            Required<CheckBox>(window, "FindHostToggle").IsChecked = true;
            query.Text.Should().BeEmpty();
            state.MatchStart.Should().Be(-1);
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            window.IsVisible.Should().BeFalse();
            state.Content.Should().BeNull();
            state.HostText.Should().BeEmpty();
        }
        finally { window.ClearAndClose(); }
    });

    [Fact]
    public Task ExactUserAndSeparateTypedHostNavigationIsInertNonwrappingAndNeverCopies() => HeadlessSession.RunAsync(() =>
    {
        var admitted = Content();
        var state = new DetailViewerState(admitted);
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
        var result = renderer.Render(admitted.Source, admitted.Kind, admitted.Reference);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback);
        var writes = 0;
        var window = new DetailWindow(state, result, (_, _, _) => { writes++; return Task.CompletedTask; });
        window.Show();
        try
        {
            var query = Required<TextBox>(window, "SearchInput");
            var span = Required<NamedTextBlock>(window, "FindSpan");
            query.Text = "alpha";
            Click(window, "FirstMatch");
            span.Text.Should().Be("alpha");
            span.IsFocused.Should().BeTrue();
            state.MatchStart.Should().Be(6);
            var peer = ControlAutomationPeer.CreatePeerForElement(span)!;
            peer.GetAutomationControlType().Should().Be(AutomationControlType.Text);
            peer.GetName().Should().Contain("Exact inert admitted detail literal match").And.Contain("alpha");
            Click(window, "NextMatch");
            state.MatchStart.Should().Be(12);
            Click(window, "NextMatch");
            state.Status.Should().Contain("End of forward direction").And.Contain("no wrap");
            span.IsVisible.Should().BeFalse();
            Click(window, "PreviousMatch");
            state.MatchStart.Should().Be(6);
            Click(window, "PreviousMatch");
            state.Status.Should().Contain("End of backward direction");
            query.Text = "ALPHA";
            Required<Button>(window, "NextMatch").IsEnabled.Should().BeFalse();
            Click(window, "FirstMatch");
            state.MatchStart.Should().Be(0);
            query.Text = "missing";
            Click(window, "FirstMatch");
            state.Status.Should().Contain("No literal match");
            Required<CheckBox>(window, "FindHostToggle").IsChecked = true;
            query.Text = "HOST";
            Click(window, "FirstMatch");
            span.Text.Should().Be("HOST");
            Required<TextBox>(window, "ContentReader").Text.Should().Be(admitted.Source);
            Required<TextBlock>(window, "ConversationHostLabel").Text.Should().Be(state.HostText);
            Click(window, "CancelFind");
            query.Text.Should().Be("HOST");
            query.IsFocused.Should().BeTrue();
            span.Text.Should().BeNull();
            Click(window, "ClearFind");
            query.Text.Should().BeEmpty();
            Required<Button>(window, "CopySource").IsEnabled.Should().BeFalse();
            Required<Button>(window, "CopySelection").IsEnabled.Should().BeFalse();
            query.Text = "private query";
            query.RaiseEvent(new RoutedEventArgs(TextBox.CopyingToClipboardEvent) { Source = query });
            query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.C, KeyModifiers = KeyModifiers.Control });
            writes.Should().Be(0);
            state.Content.Should().BeSameAs(admitted);
            state.Content!.Source.Should().Be("ALPHA alpha alpha\r\nexact USER");
        }
        finally { window.ClearAndClose(); }
    });

    [Theory]
    [InlineData("")]
    [InlineData("a\nb")]
    [InlineData("\u202ealpha")]
    [InlineData("surrogate")]
    [InlineData("characters")]
    [InlineData("bytes")]
    public Task InvalidQueriesRemainExactAndVisiblyUnavailable(string input) => HeadlessSession.RunAsync(() =>
    {
        var content = Content();
        var state = new DetailViewerState(content);
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source, content.Kind);
        var window = new DetailWindow(state, result, (_, _, _) => throw new InvalidOperationException("No clipboard authority."));
        window.Show();
        try
        {
            var query = input switch { "characters" => new string('a', 257), "bytes" => new string('界', 171),
                "surrogate" => new string('\ud800', 1), _ => input };
            Required<TextBox>(window, "SearchInput").Text = query;
            Click(window, "FirstMatch");
            state.SearchQuery.Should().Be(query);
            state.Status.Should().Contain("rejected").And.Contain("nothing was truncated");
            Required<NamedTextBlock>(window, "FindSpan").IsVisible.Should().BeFalse();
        }
        finally { window.ClearAndClose(); }
    });

    [Theory]
    [InlineData("private")]
    [InlineData("source")]
    [InlineData("replacement")]
    [InlineData("close")]
    public Task RevocationOrClosureClearsQuerySpanHostAndCannotPublishOnOldControls(string reason) => HeadlessSession.RunAsync(() =>
    {
        var content = Content();
        var current = content;
        var allowed = true;
        var state = new DetailViewerState(content, () => allowed && ReferenceEquals(current, content));
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(content.Source, content.Kind);
        var window = new DetailWindow(state, result, (_, _, _) => throw new InvalidOperationException("No clipboard authority."));
        window.Show();
        Required<TextBox>(window, "SearchInput").Text = "alpha";
        Click(window, "FirstMatch");
        if (string.Equals(reason, "close", StringComparison.Ordinal)) { window.Close(); }
        else
        {
            if (string.Equals(reason, "replacement", StringComparison.Ordinal)) { current = Content(); }
            else { allowed = false; }
            Click(window, "NextMatch");
        }
        window.IsVisible.Should().BeFalse();
        state.Content.Should().BeNull();
        state.SearchQuery.Should().BeEmpty();
        Required<TextBox>(window, "ContentReader").Text.Should().BeEmpty();
        Required<TextBlock>(window, "ConversationHostLabel").Text.Should().BeEmpty();
        Required<NamedTextBlock>(window, "FindSpan").Text.Should().BeNull();
        Click(window, "FirstMatch");
        state.MatchStart.Should().Be(-1);
        window.ClearAndClose();
    });

    private static AdmittedDetailContent Content()
    {
        var message = new ConversationMessage(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 1,
            DateTimeOffset.UnixEpoch, "ALPHA alpha alpha\r\nexact USER", null, null);
        return new(new(new(message.Request.RequestId.Value), 1), DetailContentKind.PlainText,
            DetailContentOrigin.RetainedConversation, DetailSensitivity.DisclosureConfirmationRequired,
            "Original USER", "Private retained original", message.Body, conversationMessage: message);
    }

    private static T Required<T>(DetailWindow window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException("Missing native control.");

    private static void Click(DetailWindow window, string name) =>
        Required<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
