using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

using Kora.Core.Dependencies;

namespace Kora.Core.Mcp;

/// <summary>Bounded RFC 6570 scalar expansion. URI values remain server identifiers, never local access authority.</summary>
public sealed class McpResourceTemplate
{
    private readonly string template;
    private readonly ImmutableArray<Expression> expressions;

    public McpResourceTemplate(string template)
    {
        _ = McpCapabilitySelection.ItemKey(McpCapabilityKind.ResourceTemplate, template);
        this.template = template;
        var parsed = ImmutableArray.CreateBuilder<Expression>();
        var position = 0;
        while (position < template.Length)
        {
            var open = template.IndexOf('{', position);
            if (open < 0)
            {
                if (template.AsSpan(position).Contains('}')) { throw new InvalidDataException("Unbalanced resource template."); }
                break;
            }
            if (template.AsSpan(position, open - position).Contains('}'))
            { throw new InvalidDataException("Unbalanced resource template."); }
            var close = template.IndexOf('}', open + 1);
            if (close < 0) { throw new InvalidDataException("Unbalanced resource template."); }
            var body = template[(open + 1)..close];
            var operation = body.Length > 0 && "+#./;?&".Contains(body[0], StringComparison.Ordinal) ? body[0] : '\0';
            var variables = (operation == '\0' ? body : body[1..]).Split(',');
            if (variables.Length > 8)
            { throw new InvalidDataException("The resource template has too many parameters."); }
            var names = variables.Select(variable =>
            {
                var name = variable.EndsWith('*') ? variable[..^1] : variable;
                var parts = name.Split(':');
                if (variable.EndsWith('*') && name.Contains(':', StringComparison.Ordinal))
                { throw new InvalidDataException("A resource template parameter cannot combine prefix and explode modifiers."); }
                if (parts.Length > 2 || parts[0].Length is < 1 or > 64
                    || parts[0].Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.'))
                    || parts[0].Split('.').Any(segment => segment.Length == 0)
                    || parts.Length == 2 && (!int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var prefix) || prefix is < 1 or > 1024))
                { throw new InvalidDataException("The resource template requires supported scalar parameter names."); }
                return (Name: parts[0], Prefix: parts.Length == 2 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1024);
            }).ToImmutableArray();
            parsed.Add(new(open, close + 1, operation, names));
            if (parsed.Count > 16) { throw new InvalidDataException("The resource template has too many expressions."); }
            position = close + 1;
        }
        expressions = parsed.ToImmutable();
        Parameters = expressions.SelectMany(expression => expression.Variables.Select(variable => variable.Name))
            .Distinct(StringComparer.Ordinal).ToImmutableArray();
        if (Parameters.Length > 8) { throw new InvalidDataException("The resource template has too many parameters."); }
        _ = Expand(JsonSerializer.Serialize(Parameters.ToDictionary(name => name, _ => "value", StringComparer.Ordinal)));
    }

    public ImmutableArray<string> Parameters { get; }

    public string Expand(string arguments)
    {
        using var document = ModelToolProtocol.ReadJson(Encoding.UTF8.GetBytes(arguments), McpToolSchema.MaximumArgumentsUtf8Bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("Resource template arguments must be a JSON object of strings."); }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!Parameters.Contains(property.Name, StringComparer.Ordinal) || property.Value.ValueKind != JsonValueKind.String
                || property.Value.GetString() is not { Length: <= 1024 } value || value.Any(char.IsControl))
            { throw new InvalidDataException("Resource template arguments must match the declared scalar parameters."); }
            values.Add(property.Name, value);
        }
        var result = new StringBuilder();
        var position = 0;
        foreach (var expression in expressions)
        {
            result.Append(template.AsSpan(position, expression.Start - position));
            var parts = new List<string>();
            foreach (var variable in expression.Variables)
            {
                if (!values.TryGetValue(variable.Name, out var value)) { continue; }
                value = string.Concat(value.EnumerateRunes().Take(variable.Prefix));
                var encoded = expression.Operation is '+' or '#' ? Reserved(value) : Uri.EscapeDataString(value);
                parts.Add(expression.Operation is ';' or '?' or '&'
                    ? Uri.EscapeDataString(variable.Name) + (value.Length == 0 && expression.Operation == ';' ? string.Empty : "=" + encoded)
                    : encoded);
            }
            if (parts.Count > 0)
            {
                result.Append(expression.Operation switch
                {
                    '#' => "#",
                    '.' => ".",
                    '/' => "/",
                    ';' => ";",
                    '?' => "?",
                    '&' => "&",
                    _ => string.Empty,
                });
                result.AppendJoin(expression.Operation switch { '.' => ".", '/' => "/", ';' => ";", '?' or '&' => "&", _ => "," }, parts);
            }
            position = expression.End;
        }
        result.Append(template.AsSpan(position));
        var expanded = result.ToString();
        McpCapabilityProtocol.ValidateUri(expanded);
        return expanded;
    }

    private static string Reserved(string value)
    {
        var result = new StringBuilder();
        var runes = value.EnumerateRunes().ToArray();
        for (var index = 0; index < runes.Length; index++)
        {
            var rune = runes[index];
            if (rune.Value == '%' && index + 2 < runes.Length && runes[index + 1].IsAscii && runes[index + 2].IsAscii
                && Uri.IsHexDigit((char)runes[index + 1].Value) && Uri.IsHexDigit((char)runes[index + 2].Value))
            {
                result.Append('%').Append((char)runes[++index].Value).Append((char)runes[++index].Value);
                continue;
            }
            if (rune.IsAscii && ":/?#[]@!$&'()*+,;=".Contains((char)rune.Value, StringComparison.Ordinal))
            { result.Append((char)rune.Value); }
            else { result.Append(Uri.EscapeDataString(rune.ToString())); }
        }
        return result.ToString();
    }

    private sealed record Expression(int Start, int End, char Operation, ImmutableArray<(string Name, int Prefix)> Variables);
}