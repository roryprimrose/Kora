using System.Net;
using System.Text.RegularExpressions;

using AwesomeAssertions;

using Kora.Core.Presentation;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class NativeClipboardSelectionTests
{
    [Theory]
    [InlineData("# heading", "ead", "<h1>ead</h1>")]
    [InlineData("before **bold *nested* tail** after", "nested", "<p><strong><em>nested</em></strong></p>")]
    [InlineData("before `a < b & c` after", "< b &", "<p><code>&lt; b &amp;</code></p>")]
    [InlineData("before [**label**](javascript:alert) after", "label", "<p><strong>label</strong></p>")]
    [InlineData("before [label](https://example.test/a?x=1&y=2) after", "x=1&y=2", "<p>x=1&amp;y=2</p>")]
    [InlineData("before <https://example.test/path> after", "example.test", "<p>example.test</p>")]
    [InlineData("```text\n\tone & <two>\n\tthree\n```", "one & <two>", "<pre><code>one &amp; &lt;two&gt;</code></pre>")]
    [InlineData("---", "—", "<hr>")]
    public void SelectionKeepsOnlyIntersectingSupportedFormatting(string source, string selected, string expected)
    {
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(source);
        result.IsFallback.Should().BeFalse();
        var start = result.SemanticText.IndexOf(selected, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        result.ClipboardSelection!.Serialize(start, selected.Length).Should().Be(expected);
    }

    [Fact]
    public void OrderedSelectionRetainsTheSelectedItemOrdinalAndBalancedNestedContainers()
    {
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance)
            .Render("3. hidden\n4. **selected**\n5. hidden\n\n> - *nested*");
        result.IsFallback.Should().BeFalse();
        var start = result.SemanticText.IndexOf("selected", StringComparison.Ordinal);
        var html = result.ClipboardSelection!.Serialize(start, "selected".Length);
        html.Should().Be("<ol start=\"4\"><li><p><strong>selected</strong></p></li></ol>");
        var nested = result.SemanticText.IndexOf("nested", StringComparison.Ordinal);
        result.ClipboardSelection.Serialize(nested, "nested".Length).Should()
            .Be("<blockquote><ul><li><p><em>nested</em></p></li></ul></blockquote>");
    }

    [Theory]
    [InlineData("Name", "<strong>Name</strong>", "<th>")]
    [InlineData("Alpha", "<em>Alpha</em>", "<td>")]
    [InlineData("Beta", "<code>Beta</code>", "<td>")]
    public void TableSelectionsMapActualCellsWithoutAddingUnselectedHeadersOrData(
        string selected, string formatted, string cell)
    {
        const string source = "| **Name** | State |\n| --- | --- |\n| *Alpha* | `Beta` |\n| Hidden | Unselected |";
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance, pipeTablesAvailable: true)
            .Render(source, format: NativeDetailFormat.PipeTablesV2);
        result.IsFallback.Should().BeFalse();
        var start = result.SemanticText.IndexOf(selected, StringComparison.Ordinal);
        result.ClipboardSelection!.Serialize(start, selected.Length).Should()
            .Be("<table><tr>" + cell + formatted + cell.Replace("<", "</", StringComparison.Ordinal) + "</tr></table>");
    }

    [Theory]
    [InlineData((int)NativeDetailFormat.MarkdownChecklistsV3, "- [x] **done**\n- [ ] hidden", "done", "<strong>done</strong>")]
    [InlineData((int)NativeDetailFormat.MarkdownStrikethroughV4, "before ~~**removed**~~ after", "removed", "<s><strong>removed</strong></s>")]
    public void IndependentlyAvailableProfilesKeepSelectedFormattingWithoutEnablingThem(
        int formatValue, string source, string selected, string expected)
    {
        var format = (NativeDetailFormat)formatValue;
        var unavailable = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render(source, format: format);
        unavailable.IsFallback.Should().BeTrue();
        unavailable.ClipboardSelection.Should().BeNull();
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance,
            checklistsAvailable: true, strikethroughAvailable: true).Render(source, format: format);
        result.IsFallback.Should().BeFalse();
        var start = result.SemanticText.IndexOf(selected, StringComparison.Ordinal);
        result.ClipboardSelection!.Serialize(start, selected.Length).Should().Contain(expected).And.NotContain("hidden");
    }

    [Fact]
    public void EveryRangeRetainsExactSemanticTextAndNeverIntroducesMarkupOrDestinations()
    {
        var result = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance)
            .Render("# é 😀\n\n**one *two* end** & `<tag>`\n\n> quote\n\n3. list\n4. next");
        result.IsFallback.Should().BeFalse();
        var text = result.SemanticText;
        var selection = result.ClipboardSelection!;
        for (var start = 0; start < text.Length; start++)
        {
            if (char.IsLowSurrogate(text[start])) { continue; }
            for (var length = 1; length <= text.Length - start; length++)
            {
                var end = start + length;
                if (end < text.Length && char.IsLowSurrogate(text[end])) { continue; }
                var html = selection.Serialize(start, length);
                html.Should().NotContain("<tag>").And.NotContain("href=").And.NotContain("style=");
                var decoded = WebUtility.HtmlDecode(Regex.Replace(html.Replace("<br>", "\n", StringComparison.Ordinal),
                    "<[^>]+>", string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
                decoded.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Should().Be(text.Substring(start, length).Replace("\r\n", "\n", StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, int.MaxValue)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    public void InvalidOrSplitUnicodeRangesCannotProduceASelection(int start, int length)
    {
        var selection = new NativeDetailRenderer(NullLogger<NativeDetailRenderer>.Instance).Render("😀 text").ClipboardSelection!;
        var copy = () => selection.Serialize(start, length);
        copy.Should().Throw<InvalidOperationException>();
    }
}
