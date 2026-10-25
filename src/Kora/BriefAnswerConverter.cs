using System.Globalization;

using Avalonia.Data.Converters;

using Kora.Core.Presentation;

namespace Kora;

internal sealed class BriefAnswerConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string answer ? AnswerPresentation.From(answer).Brief : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A displayed answer cannot replace immutable retained source.");
}
