using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

using Kora.Application.ViewModels;
using Kora.Core.Configuration;

namespace Kora;

internal static class NativeReaderTypography
{
    internal static void Bind(TextBlock reader, MainViewModel host)
    {
        var baseFontSize = reader.FontSize;
        var baseLineHeight = reader.LineHeight;
        reader.Bind(TextBlock.FontSizeProperty, ScaleBinding(host, baseFontSize));
        if (!double.IsNaN(baseLineHeight))
        {
            reader.Bind(TextBlock.LineHeightProperty, ScaleBinding(host, baseLineHeight));
        }
    }

    internal static void Bind(TextBox reader, MainViewModel host)
    {
        reader.Bind(TextBox.FontSizeProperty, ScaleBinding(host, reader.FontSize));
    }

    private static Binding ScaleBinding(MainViewModel host, double baseline) => new(nameof(MainViewModel.ReaderTextScalePercent))
    {
        Source = host,
        Mode = BindingMode.OneWay,
        Converter = new FuncValueConverter<int, double>(percent => baseline * percent / ReaderTextScaleSettings.DefaultPercent),
    };
}
