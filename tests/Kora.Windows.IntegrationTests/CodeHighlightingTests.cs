using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.CodeHighlighting;
using Kora.Application.Presentation;
using Kora.Application.Documentation;
using Kora.Core.Context;
using Kora.Core.Presentation;
using Kora.Core.Presentation.CodeHighlighting;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class CodeHighlightingTests
{
    [Theory]
    [InlineData(CodeLanguage.CSharp, "public class C { string s = \"é😀\"; // comment\r\n}\r\n")]
    [InlineData(CodeLanguage.PowerShell, "function X { Write-Output 'ok' # inert\n}\n")]
    [InlineData(CodeLanguage.Json, "{ \"a\": true, \"b\": 12 }")]
    [InlineData(CodeLanguage.Yaml, "# comment\na: true")]
    [InlineData(CodeLanguage.Xml, "<a value=\"x\">12</a>")]
    [InlineData(CodeLanguage.Markdown, "# Head\n`code`")]
    [InlineData(CodeLanguage.Sql, "SELECT a FROM t -- comment")]
    [InlineData(CodeLanguage.JavaScript, "const x = 'x'; // inert")]
    [InlineData(CodeLanguage.TypeScript, "interface X { value: 12 }")]
    [InlineData(CodeLanguage.Html, "<p class='a'>12</p>")]
    [InlineData(CodeLanguage.Css, ".a { color: 'red'; }")]
    [InlineData(CodeLanguage.Shell, "echo 'x' # comment")]
    [InlineData(CodeLanguage.Batch, "echo x\nrem comment\n:: inert")]
    [InlineData(CodeLanguage.Ini, "; comment\n[a]\nx=12")]
    [InlineData(CodeLanguage.Diff, "--- a\r\n+++ b\r\n-old\r\n+new\r\n context")]
    public void TokensAreStrictOrderedSpansOverUnchangedSource(CodeLanguage language, string text)
    {
        var source = new CodeSource(text, language, 17);
        var result = new OfflineCodeHighlighter(new FrozenClock()).Highlight(source, TestContext.Current.CancellationToken);
        result.Outcome.Should().Be(CodeHighlightOutcome.Highlighted);
        result.Source.Should().BeSameAs(source);
        result.Tokens.Should().NotBeEmpty();
        var position = 0;
        foreach (var token in result.Tokens)
        {
            token.Start.Should().BeGreaterThanOrEqualTo(position);
            token.Length.Should().BePositive();
            (token.Start + token.Length).Should().BeLessThanOrEqualTo(text.Length);
            position = token.Start + token.Length;
        }
        source.Text.Should().Be(text);
        source.SourceStart.Should().Be(17);
    }

    [Fact]
    public void EveryBudgetHasAnActualBoundaryAndNeverPublishesPartialTokens()
    {
        var highlighter = new OfflineCodeHighlighter(new FrozenClock());
        CodeProjection Highlight(string text, CodeLanguage language = CodeLanguage.CSharp) =>
            highlighter.Highlight(new(text, language), TestContext.Current.CancellationToken);
        Highlight(new string('a', 32768)).Outcome.Should().Be(CodeHighlightOutcome.Highlighted);
        Highlight(new string('a', 32769)).Outcome.Should().Be(CodeHighlightOutcome.ByteLimit);
        Highlight(new string('é', 16384)).Outcome.Should().Be(CodeHighlightOutcome.Highlighted);
        Highlight(new string('é', 16385)).Outcome.Should().Be(CodeHighlightOutcome.ByteLimit);
        Highlight(new string('\n', 1023)).Outcome.Should().Be(CodeHighlightOutcome.Highlighted);
        Highlight(new string('\n', 1024)).Outcome.Should().Be(CodeHighlightOutcome.LineLimit);
        Highlight(string.Concat(Enumerable.Repeat("\r\n", 1023))).Outcome.Should().Be(CodeHighlightOutcome.Highlighted);
        Highlight(new string(';', 2048)).Tokens.Should().HaveCount(2048);
        Highlight(new string(';', 2049)).Outcome.Should().Be(CodeHighlightOutcome.TokenLimit);
        Highlight(new string('(', 64)).Outcome.Should().Be(CodeHighlightOutcome.Highlighted);
        Highlight(new string('(', 65)).Outcome.Should().Be(CodeHighlightOutcome.NestingLimit);
        Highlight("\ud800").Outcome.Should().Be(CodeHighlightOutcome.InvalidUnicode);
        Highlight("anything", CodeLanguage.PlainText).Outcome.Should().Be(CodeHighlightOutcome.Unsupported);
        highlighter.Highlight(new("text", CodeLanguage.CSharp), new CancellationToken(true))
            .Outcome.Should().Be(CodeHighlightOutcome.Cancelled);
        var timed = new OfflineCodeHighlighter(new ExpiringClock()).Highlight(
            new("const x = 1;", CodeLanguage.JavaScript), TestContext.Current.CancellationToken);
        timed.Outcome.Should().Be(CodeHighlightOutcome.TimeLimit);
        timed.Tokens.Should().BeEmpty();
        foreach (var text in new[] { new string(';', 2049), new string('(', 65), "\ud800", new string('\n', 1024) })
        { Highlight(text).Tokens.Should().BeEmpty(); }
    }

    [Theory]
    [InlineData(CodeLanguage.CSharp, "/*\r\ncomment { 123 \"\r\n*/")]
    [InlineData(CodeLanguage.PowerShell, "<#\ncomment\n#>")]
    [InlineData(CodeLanguage.Xml, "<!--\ncomment\n-->")]
    [InlineData(CodeLanguage.Html, "<!--\ncomment\n-->")]
    [InlineData(CodeLanguage.CSharp, "/* unterminated")]
    [InlineData(CodeLanguage.CSharp, "\"escaped \\\" quote\"")]
    public void MultilineCommentsAndEscapedStringsAreOneExactToken(CodeLanguage language, string text)
    {
        var result = new OfflineCodeHighlighter(new FrozenClock()).Highlight(new(text, language), TestContext.Current.CancellationToken);
        result.Tokens.Should().ContainSingle().Which.Should().Be(
            new CodeToken(0, text.Length, text[0] == '"' ? CodeTokenKind.String : CodeTokenKind.Comment));
        var overLines = "/*" + new string('\n', 1024) + "*/";
        new OfflineCodeHighlighter(new FrozenClock()).Highlight(new(overLines, CodeLanguage.CSharp),
            TestContext.Current.CancellationToken).Outcome.Should().Be(CodeHighlightOutcome.LineLimit);
    }

    [Fact]
    public void CancellationAndDeadlineDuringRealTokenizationDiscardPartialSpans()
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new CallbackClock(cancellation.Cancel);
        var source = new CodeSource("public class C { }", CodeLanguage.CSharp);
        var result = new OfflineCodeHighlighter(clock).Highlight(source, cancellation.Token);
        result.Outcome.Should().Be(CodeHighlightOutcome.Cancelled);
        result.Tokens.Should().BeEmpty();
        result.Source.Should().BeSameAs(source);
    }

    [Fact]
    public Task RetirementAndReplacementSuppressLateSourceAndJoinActualWorkers() => HeadlessSession.RunAsync(async () =>
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = false;
        var callerThread = Environment.CurrentManagedThreadId;
        var workerThread = callerThread;
        var session = new CodeHighlightSession((source, token) =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            if (string.Equals(source.Text, "old", StringComparison.Ordinal))
            {
                entered.Set();
                release.Wait(CancellationToken.None);
                observedCancellation = token.IsCancellationRequested;
                stopped.SetResult();
            }
            return new(source, CodeHighlightOutcome.Highlighted, []);
        });
        var old = session.RenderAsync(new("old", CodeLanguage.CSharp));
        await Task.Run(() => entered.Wait());
        var replacement = await session.RenderAsync(new("new", CodeLanguage.Json));
        replacement!.Source.Text.Should().Be("new");
        var join = session.JoinAsync();
        join.IsCompleted.Should().BeFalse();
        release.Set();
        await join;
        await stopped.Task;
        (await old).Should().BeNull();
        observedCancellation.Should().BeTrue();
        workerThread.Should().NotBe(callerThread);
    });

    [Fact]
    public Task TokenizerFailureIsVisibleAndDoesNotDiscardSource() => HeadlessSession.RunAsync(async () =>
    {
        var session = new CodeHighlightSession((_, _) => throw new InvalidOperationException("not logged content"));
        var source = new CodeSource("private source", CodeLanguage.CSharp);
        var result = await session.RenderAsync(source);
        result!.Outcome.Should().Be(CodeHighlightOutcome.Failed);
        result.Source.Should().BeSameAs(source);
        result.Tokens.Should().BeEmpty();
        await session.JoinAsync();
    });

    [Fact]
    public Task NativeProjectionPreservesSourceAndThemeToggleRetirement() => HeadlessSession.RunAsync(async () =>
    {
        const string source = "```cs\r\npublic class C {\r\n\tstring x = \"é😀\";\r\n}\r\n```\r\n";
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(source);
        var code = result.Sections.Single().Code!;
        code.Text.Should().Be("public class C {\r\n\tstring x = \"é😀\";\r\n}\r\n");
        source.Substring(code.SourceStart, code.Text.Length).Should().Be(code.Text);
        result.Source.Should().Be(source);
        var block = (NativeCodeBlock)MarkdownDocumentRenderer.CreateControls(result, selectable: false).Single();
        block.Reader.Should().NotBeOfType<SelectableTextBlock>();
        var window = new Window { Content = block, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        await block.Pending;
        block.Status.Should().Contain("Highlighted");
        InlineText(block.Reader).Should().Be(code.Text);
        var light = ((Run)block.Reader.Inlines!.First()).Foreground;
        window.RequestedThemeVariant = ThemeVariant.Dark;
        ((Run)block.Reader.Inlines!.First()).Foreground.Should().NotBe(light);
        block.PlainToggle.IsChecked = true;
        block.Reader.Text.Should().Be(code.Text);
        result.Source.Should().Be(source);
        block.PlainToggle.IsChecked = false;
        InlineText(block.Reader).Should().Be(code.Text);
        window.Close();
        await block.JoinAsync();
        block.Reader.Text.Should().BeEmpty();
        block.Status.Should().Contain("retired");
    });

    [Fact]
    public Task UnsupportedAndLargeViewsKeepHonestPlainFallback() => HeadlessSession.RunAsync(async () =>
    {
        var renderer = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance);
        var result = renderer.Render("```unknown\n$secret\n```");
        var block = (NativeCodeBlock)MarkdownDocumentRenderer.CreateControls(result).Single();
        var window = new Window { Content = block };
        window.Show();
        await block.Pending;
        block.Status.Should().Contain("Unsupported").And.Contain("plain");
        block.Reader.Text.Should().Be("$secret\n");
        window.Close();
        await block.JoinAsync();
        var large = renderer.Render("```cs\n" + new string('a', 32769) + "\n```");
        var largeBlock = (NativeCodeBlock)MarkdownDocumentRenderer.CreateControls(large).Single();
        largeBlock.Reader.Should().BeOfType<SelectableTextBlock>();
        largeBlock.Status.Should().Contain("ViewLimit").And.Contain("plain");
        renderer.Render("```mermaid\ngraph TD; A-->B\n```").IsFallback.Should().BeTrue();
    });

    [Fact]
    public Task PrivateDetailPlainSourceFindAndCopyKeepOriginalIdentityAndRetireOnRevocation() => HeadlessSession.RunAsync(async () =>
    {
        const string text = "```cs\r\npublic class C { }\r\n```\r\n";
        var content = new AdmittedDetailContent(new(new(Guid.NewGuid()), 1), DetailContentKind.Markdown,
            DetailContentOrigin.EmbeddedDocument, DetailSensitivity.DisclosureConfirmationRequired,
            "Code", "Admitted immutable test source", text);
        var digest = content.Digest;
        var admitted = true;
        var state = new DetailViewerState(content, () => admitted);
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(text);
        state.CompleteRender(state.Generation, result.SemanticText, result.Status, result.IsFallback);
        var copies = 0;
        var view = new DetailContentView(state, result, (_, _, _) => { copies++; return Task.CompletedTask; });
        var window = new Window { Content = view };
        window.Show();
        var code = view.GetVisualDescendants().OfType<NativeCodeBlock>().Single();
        await code.Pending;
        try
        {
            code.PlainToggle.IsChecked = true;
            state.TryGetCopySource(true, false, out var denied).Should().BeFalse();
            denied.Should().BeNull();
            state.TryGetCopySource(true, true, out var original).Should().BeTrue();
            original.Should().Be(text);
            view.FindControl<CheckBox>("SourceToggle")!.IsChecked = true;
            state.ActiveText.Should().Be(text);
            state.Find("public", SnapshotFindDirection.First).Should().BeTrue();
            state.MatchStart.Should().Be(text.IndexOf("public", StringComparison.Ordinal));
            content.Digest.Should().Be(digest);
            state.Reference.Should().Be(content.Reference);
            copies.Should().Be(0);
            admitted = false;
            state.Content.Should().BeNull();
            await code.JoinAsync();
            code.Reader.Text.Should().BeEmpty();
            code.Status.Should().Contain("retired");
            state.TryGetCopySource(true, true, out _).Should().BeFalse();
        }
        finally
        {
            view.ClearPresentation();
            window.Close();
            await code.JoinAsync();
        }
    });

    [Theory]
    [InlineData("😀 publić = 12;\r\n", CodeLanguage.CSharp)]
    [InlineData("\tα = 'é';\r\n", CodeLanguage.PowerShell)]
    public void NoTokenBoundarySplitsASurrogatePair(string text, CodeLanguage language)
    {
        var result = new OfflineCodeHighlighter(new FrozenClock()).Highlight(new(text, language),
            TestContext.Current.CancellationToken);
        foreach (var token in result.Tokens)
        {
            char.IsLowSurrogate(text[token.Start]).Should().BeFalse();
            char.IsHighSurrogate(text[token.Start + token.Length - 1]).Should().BeFalse();
        }
    }

    [Fact]
    public void AllTokenColorsMeetTextContrastOnTheirEffectiveThemeSurface()
    {
        foreach (var kind in Enum.GetValues<CodeTokenKind>())
        {
            foreach (var dark in new[] { false, true })
            {
                var color = ((SolidColorBrush)NativeCodeBlock.Brush(kind, dark)).Color;
                var foreground = Luminance(color);
                foreach (var surface in dark ? new[] { "#101216", "#181C23", "#171A24" }
                    : new[] { "#F4F5F8", "#FFFFFF", "#EEF0F5" })
                {
                    var background = Luminance(Color.Parse(surface));
                    ((Math.Max(foreground, background) + 0.05) / (Math.Min(foreground, background) + 0.05))
                        .Should().BeGreaterThanOrEqualTo(4.5);
                }
            }
        }
    }

    [Fact]
    public void EmbeddedGuideDescribesAvailableControlsAndUnavailableBoundaries()
    {
        var guide = new EmbeddedUserDocumentationProvider().GetPages()
            .Single(page => string.Equals(page.Id, "code-highlighting", StringComparison.Ordinal)).Markdown;
        guide.Should().Contain("**Plain code**").And.Contain("**Exact source**")
            .And.Contain("32 KiB UTF-8").And.Contain("100 ms")
            .And.Contain("No code is executed").And.Contain("does not introduce a standalone file-opening route")
            .And.Contain("not installed screen-reader or accessibility qualification");
    }

    private static string InlineText(TextBlock block) =>
        string.Concat(block.Inlines!.Cast<Run>().Select(run => run.Text));

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    private sealed class ExpiringClock : TimeProvider
    {
        private long calls;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => calls++ * 100;
    }

    private sealed class CallbackClock(Action callback) : TimeProvider
    {
        private int calls;
        public override long GetTimestamp()
        {
            if (++calls == 3) { callback(); }
            return 0;
        }
    }
}
