using System.Globalization;

using Avalonia.Data.Converters;

using Kora.Core.Conversation;

namespace Kora;

internal sealed class ObservationChoicesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ConversationMessage { Observations: { } observations } message
            ? observations.Items.Select((item, index) => new ObservationDetailChoice(message, item, index + 1)).ToArray()
            : Array.Empty<ObservationDetailChoice>();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Observed result links cannot write retained content.");
}
