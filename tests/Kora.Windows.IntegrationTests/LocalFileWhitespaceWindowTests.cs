using System.Diagnostics;
using System.Text;

using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class LocalFileWhitespaceWindowTests
{
    private const string Inspect = "Inspect whitespace in this admitted text";
    private const string ResultName = "Decoded current admitted text whitespace counts";

    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("retained")]
    public async Task ExplicitAccessibleCountsUseOnlyTheDisplayedImmutableTextWithoutReadsOrFindEffects(string surface)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var folder = Folder("\t \r\n\r\nx\nx\r\u00a0🙂", "  \t");
            var file = folder.Files[0];
            var failures = new List<string>();
            var window = new LocalFilePreviewWindow(failures.Add, captureWhitespaceAdmission: AdmittedHost);
            static Task<LocalFileSearchResult> Search(string _, LocalFileRetrievalMode mode, CancellationToken token) =>
                throw new InvalidOperationException("Whitespace inspection must not search or invoke a model.");
            switch (surface)
            {
                case "folder": window.ShowFolderRevision(folder, Search, () => true, () => throw new InvalidOperationException("No refresh.")); break;
                case "retained":
                    window.ShowAttachment(new(file.Review.Request.SessionId, new(1), new(1), file),
                        Search, () => true, () => throw new InvalidOperationException("No remove."),
                        resolveCurrent: _ => throw new InvalidOperationException("No retained read channel access."));
                    break;
                default: window.ShowRevision(file, Search, () => true, () => throw new InvalidOperationException("No reread.")); break;
            }
            window.Show();
            window.UpdateLayout();
            try
            {
                var status = Status(window);
                status.Text.Should().BeNull();
                var query = window.GetVisualDescendants().OfType<TextBox>().First();
                query.Text = "x";
                status.Text.Should().BeNull();
                var source = Source(window);
                source.Text.Should().BeSameAs(file.Text);
                var button = Button(window, Inspect);
                ControlAutomationPeer.CreatePeerForElement(button)!.GetName().Should().Be(Inspect);
                Click(button);
                status.Text.Should().Contain("CRLF pairs: 2; standalone LF: 1; standalone CR: 1; TAB (U+0009): 1; ordinary SPACE (U+0020): 1");
                ControlAutomationPeer.CreatePeerForElement(status)!.GetName().Should().Contain(ResultName).And.Contain(status.Text);
                source.Text.Should().BeSameAs(file.Text);
                query.Text.Should().Be("x");
                window.GetVisualDescendants().OfType<TextBox>().Should().NotContain(box => box.Text == file.Text);
                if (string.Equals(surface, "folder", StringComparison.Ordinal))
                {
                    var selector = window.GetVisualDescendants().OfType<ComboBox>().Single(box =>
                        AutomationProperties.GetName(box)?.StartsWith("Inspect each", StringComparison.Ordinal) == true);
                    selector.SelectedItem = folder.Files[1];
                    status.Text.Should().BeNull();
                    Click(button);
                    status.Text.Should().Contain("CRLF pairs: 0; standalone LF: 0; standalone CR: 0; TAB (U+0009): 1; ordinary SPACE (U+0020): 2");
                    source.Text.Should().BeSameAs(folder.Files[1].Text);
                }
                Click(Button(window, "Clear whitespace counts"));
                status.Text.Should().BeNull();
                Click(button);
                status.Text.Should().NotBeNullOrEmpty();
                window.ClearAndClose();
                status.Text.Should().BeNull();
                Click(button);
                status.Text.Should().BeNull();
                failures.Should().BeEmpty();
            }
            finally { window.ClearAndClose(); }
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("\ufeff")]
    public async Task EmptyAdmittedDecodedTextTruthfullyCountsZero(string text)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = Window(Folder(text).Files[0]);
            window.Show();
            window.UpdateLayout();
            try
            {
                Click(Button(window, Inspect));
                Status(window).Text.Should().Contain("CRLF pairs: 0; standalone LF: 0; standalone CR: 0; TAB (U+0009): 0; ordinary SPACE (U+0020): 0");
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task MissingHostAdmissionIsUnavailableEvenForAnAdmittedEmptyFile()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("Native outcome only."));
            window.ShowRevision(Folder("").Files[0], NoSearch, () => true, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                Click(Button(window, Inspect));
                Status(window).Text.Should().Contain("unavailable; no counts claimed").And.NotContain("CRLF pairs:");
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("different")]
    [InlineData("disabled")]
    [InlineData("hidden")]
    [InlineData("replacement")]
    [InlineData("closed")]
    public async Task NativeLifetimeOrDisplayedSourceChangesRetireCountsAndOldControlsCannotRecreateThem(string change)
    {
        await HeadlessSession.RunAsync(() =>
        {
            var window = Window(Folder("x \t\r\n").Files[0]);
            window.Show();
            window.UpdateLayout();
            try
            {
                var status = Status(window);
                var button = Button(window, Inspect);
                Click(button);
                status.Text.Should().Contain("CRLF pairs: 1");
                switch (change)
                {
                    case "missing": Source(window).Text = null; break;
                    case "different": Source(window).Text = "not admitted"; break;
                    case "disabled": window.IsEnabled = false; break;
                    case "hidden": window.Hide(); break;
                    case "replacement": window.ShowRevision(Folder("new").Files[0], NoSearch, () => true, () => Task.CompletedTask); break;
                    case "closed": window.ClearAndClose(); break;
                }
                status.Text.Should().BeNull();
                Click(button);
                status.Text.Should().NotContain("CRLF pairs:");
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("reentrant")]
    [InlineData("replace")]
    [InlineData("failure")]
    public async Task RevocationReentrancyReplacementAndUnexpectedFailureNeverPublishCounts(string boundary)
    {
        await HeadlessSession.RunAsync(() =>
        {
            LocalFilePreviewWindow? window = null;
            var calls = 0;
            bool Current()
            {
                calls++;
                if (string.Equals(boundary, "failure", StringComparison.Ordinal)) { throw new InvalidOperationException("PRIVATE source-bearing message"); }
                if (string.Equals(boundary, "before", StringComparison.Ordinal)) { return false; }
                if (string.Equals(boundary, "after", StringComparison.Ordinal)) { return calls < 2; }
                if (calls == 1 && window is not null)
                {
                    if (string.Equals(boundary, "reentrant", StringComparison.Ordinal)) { Click(Button(window, Inspect)); }
                    else { window.ShowRevision(Folder("different source").Files[0], NoSearch, () => true, () => Task.CompletedTask); }
                }
                return true;
            }
            window = Window(Folder("secret \t\r\n").Files[0], Current);
            window.Show();
            window.UpdateLayout();
            try
            {
                var status = Status(window);
                Click(Button(window, Inspect));
                status.Text.Should().NotContain("CRLF pairs:").And.NotContain("PRIVATE").And.NotContain("secret");
                if (string.Equals(boundary, "failure", StringComparison.Ordinal)) { status.Text.Should().Contain("failed; no counts claimed"); }
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task InspectionAndClearingDoNotChangeExistingFindQueryMatchOrCitations()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            var window = Window(Folder("x x \t\r\n").Files[0]);
            window.Show();
            window.UpdateLayout();
            try
            {
                var query = window.GetVisualDescendants().OfType<TextBox>().Single(box =>
                    AutomationProperties.GetName(box)?.StartsWith("Case-sensitive literal Find", StringComparison.Ordinal) == true);
                query.Text = "x";
                await ((AsyncCommand)Button(window, "Find first").Command!).ExecuteAsync();
                var match = window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
                    AutomationProperties.GetName(block)?.StartsWith("Exact literal original", StringComparison.Ordinal) == true);
                var findStatus = window.GetVisualDescendants().OfType<NamedTextBlock>().Single(block =>
                    string.Equals(AutomationProperties.GetName(block), "Literal Find outcome, not ranked retrieval", StringComparison.Ordinal));
                var before = findStatus.Text;
                Click(Button(window, Inspect));
                Click(Button(window, "Clear whitespace counts"));
                match.Text.Should().Be("x");
                findStatus.Text.Should().Be(before);
                query.Text.Should().Be("x");
                Source(window).Text.Should().Be("x x \t\r\n");
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Theory]
    [InlineData("input")]
    [InlineData("owner")]
    [InlineData("dispose")]
    public async Task OriginalHostInputOwnerAndDisposedBindingCannotBeRenewedByAnOldButton(string boundary)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = NativeUxTestFixture.Create(Path.GetTempPath(), silentCaption: true);
            await fixture.InitializeAsync();
            var main = fixture.Main;
            var file = Folder("private \t\r\n").Files[0];
            var logger = new RecordingLogger();
            var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("Native outcome only."), main, logger);
            window.ShowRevision(file, NoSearch, () => true, () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                var response = main.ResponseBody;
                var transcript = main.Transcript;
                var status = Status(window);
                var button = Button(window, Inspect);
                Click(button);
                logger.Messages.Should().BeEmpty();
                status.Text.Should().Contain("CRLF pairs: 1");
                switch (boundary)
                {
                    case "input": main.CommandText = "changed"; break;
                    case "owner": main.BindCallOwnershipGate(() => true); break;
                    case "dispose": main.Dispose(); break;
                }
                Click(button);
                status.Text.Should().NotContain("CRLF pairs:");
                main.ResponseBody.Should().Be(response);
                main.Transcript.Should().Be(transcript);
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task InspectionUsesSourceHostIdentityAndCausalLinkNotIncomingTraceAuthority()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var stopped = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, "Kora.Desktop", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = stopped.Add,
            };
            ActivitySource.AddActivityListener(listener);
            LocalFileRevision file;
            ActivityContext cause;
            using (var admission = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
                HostActivityLayer.Desktop, HostOperation.Request))
            {
                cause = admission.Activity!.Context;
                file = Folder("private \t\r\n").Files[0];
                admission.Complete(HostOperationOutcome.Completed);
            }
            var window = Window(file);
            window.Show();
            window.UpdateLayout();
            try
            {
                using var incoming = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
                    HostActivityLayer.Desktop, HostOperation.Request);
                Click(Button(window, Inspect));
                var activity = stopped.Single(item => string.Equals(item.OperationName, "presentation.update", StringComparison.Ordinal));
                activity.ParentId.Should().BeNull();
                activity.TraceId.Should().NotBe(incoming.Activity!.TraceId);
                activity.GetTagItem("kora.session.id").Should().Be(file.Review.Request.SessionId.Value);
                activity.GetTagItem("kora.task.id").Should().Be(file.Review.Request.TaskId.Value);
                activity.Status.Should().Be(ActivityStatusCode.Ok);
                activity.Links.Should().ContainSingle().Which.Context.Should().Be(cause);
                activity.TagObjects.Select(item => item.Key).Should().NotContain(key => key.Contains("count", StringComparison.Ordinal));
                activity.TagObjects.Select(item => item.Value).OfType<string>().Should()
                    .NotContain(value => value.Contains("private", StringComparison.Ordinal));
                HostActivity.Current.Should().BeSameAs(incoming);
            }
            finally { window.ClearAndClose(); }
        });
    }

    [Fact]
    public async Task UnexpectedFailureLogsOnlyTypedDiagnosticAndDisplaysOnlyNativeRecovery()
    {
        await HeadlessSession.RunAsync(() =>
        {
            var logger = new RecordingLogger();
            var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("Must remain native."), logger: logger,
                captureWhitespaceAdmission: AdmittedHost);
            window.ShowRevision(Folder("PRIVATE_BODY \r\n").Files[0], NoSearch,
                () => throw new InvalidOperationException("PRIVATE_PATH_OR_BODY"), () => Task.CompletedTask);
            window.Show();
            window.UpdateLayout();
            try
            {
                Click(Button(window, Inspect));
                Status(window).Text.Should().Be("Whitespace inspection failed; no counts claimed. Close and inspect the current source again.");
                logger.Messages.Should().ContainSingle().Which.Should()
                    .Be("Native local file whitespace inspection failed: InvalidOperationException. No counts claimed.");
                logger.Exceptions.Should().ContainSingle().Which.Should().BeNull();
            }
            finally { window.ClearAndClose(); }
        });
    }

    private static LocalFilePreviewWindow Window(LocalFileRevision file, Func<bool>? current = null)
    {
        var window = new LocalFilePreviewWindow(_ => throw new InvalidOperationException("No main response."),
            captureWhitespaceAdmission: AdmittedHost);
        window.ShowRevision(file, NoSearch, current ?? (static () => true), () => Task.CompletedTask);
        return window;
    }

    private static Task<LocalFileSearchResult> NoSearch(string query, LocalFileRetrievalMode mode, CancellationToken token) =>
        throw new InvalidOperationException("No source reads, search or model.");
    private static Func<bool> AdmittedHost() => static () => true;
    private static NamedTextBlock Status(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(block => string.Equals(AutomationProperties.GetName(block), ResultName, StringComparison.Ordinal));
    private static NamedTextBlock Source(Window window) => window.GetVisualDescendants().OfType<NamedTextBlock>()
        .Single(block => string.Equals(AutomationProperties.GetName(block), "Untrusted local file plain text content", StringComparison.Ordinal));
    private static Button Button(Window window, string caption) => window.GetVisualDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, caption));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    private static LocalFolderRevision Folder(params string[] texts)
    {
        var source = Guid.NewGuid();
        var reviewId = Guid.NewGuid();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var metadata = texts.Select((text, index) => new LocalFileMetadata($@"C:\Synthetic\{index}.md", $"file-{index}",
            Encoding.UTF8.GetByteCount(text), DateTimeOffset.UnixEpoch)).ToArray();
        var review = new LocalFolderReview(reviewId, source, request, new(@"C:\Synthetic", "folder", metadata));
        return new(review, texts.Select((text, index) => new LocalFileRevision(new(reviewId, source, request, metadata[index],
            Activity.Current?.Context ?? default),
            Encoding.UTF8.GetBytes(text), DateTimeOffset.UnixEpoch)));
    }

    private sealed class RecordingLogger : ILogger<LocalFilePreviewWindow>
    {
        internal List<string> Messages { get; } = [];
        internal List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
