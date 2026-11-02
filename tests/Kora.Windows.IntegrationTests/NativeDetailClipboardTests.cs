using System.Globalization;
using System.Text;

using Avalonia.Input;
using AwesomeAssertions;

namespace Kora.Windows.IntegrationTests;

public sealed class NativeDetailClipboardTests
{
    [Theory]
    [InlineData("<h1>Heading</h1><p>\u00e9 \U0001f600 &amp; text</p>")]
    [InlineData(null)]
    public async Task WritesOneTransferWithExactUnicodeFallbackAndCorrectUtf8HtmlBoundaries(string? fragment)
    {
        const string plain = "# Original\r\n\u00e9 \U0001f600\ttext";
        using var clipboard = new Clipboard();
        await NativeDetailClipboard.WriteDataAsync(clipboard.SetDataAsync, plain, fragment);
        clipboard.Writes.Should().Be(1);
        var transfer = clipboard.Current.Should().BeOfType<DataTransfer>().Subject;
        transfer.Items.Should().ContainSingle();
        transfer.TryGetText().Should().Be(plain);
        if (fragment is null)
        {
            transfer.Formats.Should().Equal(DataFormat.Text);
            return;
        }
        transfer.Formats.Should().BeEquivalentTo<DataFormat>([DataFormat.Text, NativeDetailClipboard.HtmlFormat]);
        NativeDetailClipboard.HtmlFormat.Identifier.Should().Be("HTML Format");
        var bytes = transfer.TryGetValue(NativeDetailClipboard.HtmlFormat)!;
        var text = Encoding.UTF8.GetString(bytes);
        int Offset(string field)
        {
            var start = text.IndexOf(field + ":", StringComparison.Ordinal) + field.Length + 1;
            return int.Parse(text.AsSpan(start, 10), CultureInfo.InvariantCulture);
        }
        var startHtml = Offset("StartHTML");
        var endHtml = Offset("EndHTML");
        var startFragment = Offset("StartFragment");
        var endFragment = Offset("EndFragment");
        text.Should().StartWith("Version:1.0\r\n");
        Encoding.UTF8.GetString(bytes.AsSpan(startFragment, endFragment - startFragment)).Should().Be(fragment);
        Encoding.UTF8.GetString(bytes.AsSpan(startHtml, startFragment - startHtml))
            .Should().Be("<html><body><!--StartFragment-->");
        Encoding.UTF8.GetString(bytes.AsSpan(endFragment, endHtml - endFragment))
            .Should().Be("<!--EndFragment--></body></html>");
        endHtml.Should().Be(bytes.Length - 1);
        bytes[^1].Should().Be(0);
    }

    [Fact]
    public async Task FailedMultiFormatWritePropagatesWithoutPlainTextRetryOrReplacingPreviousClipboard()
    {
        using var clipboard = new Clipboard();
        await NativeDetailClipboard.WriteDataAsync(clipboard.SetDataAsync, "previous exact source", "<p>previous</p>");
        var previous = clipboard.Current;
        clipboard.Fail = true;
        var write = () => NativeDetailClipboard.WriteDataAsync(clipboard.SetDataAsync, "new source", "<h1>new</h1>");
        await write.Should().ThrowAsync<IOException>();
        clipboard.Current.Should().BeSameAs(previous);
        clipboard.Writes.Should().Be(2);
        ((DataTransfer)clipboard.Current!).TryGetText().Should().Be("previous exact source");
    }

    private sealed class Clipboard : IDisposable
    {
        internal int Writes { get; private set; }
        internal bool Fail { get; set; }
        internal IAsyncDataTransfer? Current { get; private set; }
        public Task SetDataAsync(IAsyncDataTransfer? dataTransfer)
        {
            Writes++;
            if (Fail) { throw new IOException("Deterministic clipboard write failure."); }
            Current?.Dispose();
            Current = dataTransfer;
            return Task.CompletedTask;
        }
        public void Dispose() => Current?.Dispose();
    }
}
