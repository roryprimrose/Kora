using System.Globalization;
using System.Text.RegularExpressions;

using Kora.Core.Configuration;

namespace Kora.Windows.IntegrationTests;

/// <summary>Resolves raw XAML assistant-name bindings to the text shown with the default name.</summary>
internal static partial class AssistantNameXaml
{
    internal static string? Resolve(string? value)
    {
        if (value is null) { return null; }
        var match = Binding().Match(value);
        if (!match.Success) { return value; }
        if (!match.Groups["format"].Success) { return AssistantNameRules.DefaultName; }
        var format = match.Groups["format"].Value;
        if (format.StartsWith("{}", StringComparison.Ordinal)) { format = format[2..]; }
        return string.Format(CultureInfo.InvariantCulture, format, AssistantNameRules.DefaultName);
    }

    [GeneratedRegex(@"^\{Binding Name, Source=\{x:Static assistant:AssistantNameDisplay\.Current\}(, StringFormat='(?<format>.*)')?\}$", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex Binding();
}
