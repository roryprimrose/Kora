using System.Globalization;
using System.Text;

using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Kora;

internal sealed class NativeDetailClipboard : IDetailClipboard
{
    internal static readonly DataFormat<byte[]> HtmlFormat = DataFormat.CreateBytesPlatformFormat("HTML Format");

    public Task WriteAsync(IDetailView view, string source, string? htmlFragment)
    {
        var clipboard = (view as DetailWindow)?.Clipboard
            ?? throw new InvalidOperationException("The native clipboard is unavailable.");
        return WriteDataAsync(clipboard.SetDataAsync, source, htmlFragment);
    }

    internal static async Task WriteDataAsync(Func<IAsyncDataTransfer, Task> write, string source, string? htmlFragment)
    {
        var data = new DataTransfer();
        var item = new DataTransferItem();
        item.SetText(source);
        if (htmlFragment is not null) { item.Set(HtmlFormat, SerializeHtml(htmlFragment)); }
        data.Add(item);
        var transferred = false;
        try
        {
            await write(data);
            transferred = true;
        }
        finally
        {
            // Successful SetDataAsync transfers lifetime ownership to Avalonia.
            if (!transferred) { ((IAsyncDataTransfer)data).Dispose(); }
        }
    }

    private static byte[] SerializeHtml(string fragment)
    {
        const string header = "Version:1.0\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string prefix = "<html><body><!--StartFragment-->";
        const string suffix = "<!--EndFragment--></body></html>";
        var utf8 = new UTF8Encoding(false, true);
        var startHtml = utf8.GetByteCount(string.Format(CultureInfo.InvariantCulture, header, 0, 0, 0, 0));
        var startFragment = startHtml + utf8.GetByteCount(prefix);
        var endFragment = startFragment + utf8.GetByteCount(fragment);
        var endHtml = endFragment + utf8.GetByteCount(suffix);
        return utf8.GetBytes(string.Format(CultureInfo.InvariantCulture, header,
            startHtml, endHtml, startFragment, endFragment) + prefix + fragment + suffix + "\0");
    }
}
