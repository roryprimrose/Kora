using System.Collections.Immutable;
using System.Text.Json;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;

namespace Kora.Core.Skills;

/// <summary>One volatile, closed, data-only example for a native v1 tool-free skill.</summary>
public sealed class OwnedSkillFixture
{
    public const int MaximumUtf8Bytes = 8192;
    public const int MaximumInputUtf8Bytes = 4096;
    public const int MaximumAssertionUtf8Bytes = 1024;
    public const int MaximumAssertions = 8;

    private OwnedSkillFixture(string source, string input, ImmutableArray<Assertion> assertions)
    { Source = source; Input = input; Assertions = assertions; }

    public string Source { get; }
    public string Input { get; }
    public ImmutableArray<Assertion> Assertions { get; }

    public static OwnedSkillFixture Parse(string source)
    {
        RequireText(source, MaximumUtf8Bytes);
        try
        {
            using var json = JsonDocument.Parse(source, new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            RequireFields(root, ["input", "assertions"]);
            var input = String(root, "input");
            RequireText(input, MaximumInputUtf8Bytes);
            var items = root.GetProperty("assertions");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > MaximumAssertions)
            { throw new InvalidDataException("Supply one to eight literal assertions."); }
            var assertions = ImmutableArray.CreateBuilder<Assertion>();
            foreach (var item in items.EnumerateArray())
            {
                RequireFields(item, ["kind", "value"]);
                var kind = String(item, "kind");
                if (kind is not ("equals" or "contains"))
                { throw new InvalidDataException("Unknown assertion denied. Only ordinal equals and contains are supported."); }
                var value = String(item, "value");
                RequireText(value, MaximumAssertionUtf8Bytes);
                assertions.Add(new(kind, value));
            }
            return new(source, input, assertions.ToImmutable());
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        { throw new InvalidDataException("Malformed fixture. No fields were ignored, repaired or truncated.", exception); }
    }

    public ImmutableArray<AssertionResult> Evaluate(string output)
    {
        RequireText(output, ModelContextEnvelope.MaximumOutputUtf8Bytes);
        return Assertions.Select(assertion => new AssertionResult(assertion,
            assertion.Kind is "equals" ? string.Equals(output, assertion.Value, StringComparison.Ordinal)
                : output.Contains(assertion.Value, StringComparison.Ordinal))).ToImmutableArray();
    }

    private static void RequireFields(JsonElement element, string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
        { throw new InvalidDataException("Fixture objects require their exact closed fields."); }
        var names = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (names.Length != fields.Length || !names.Order(StringComparer.Ordinal).SequenceEqual(fields.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        { throw new InvalidDataException("Unknown, duplicate or missing fixture fields denied. Tool responses, scripts and validators are unsupported."); }
    }

    private static string String(JsonElement element, string field)
    {
        var value = element.GetProperty(field);
        if (value.ValueKind != JsonValueKind.String)
        { throw new InvalidDataException("Fixture input, assertion kind and value must be strings."); }
        return value.GetString()!;
    }

    private static void RequireText(string text, int maximum)
    {
        var measured = ConversationText.Measure(text);
        if (measured.Utf8Bytes is not { } bytes || bytes > maximum || measured.HasUnsupportedContent || measured.IsWhiteSpace)
        { throw new InvalidDataException("Fixture text/output must be nonempty well-formed text within its complete UTF-8 limit. Nothing was truncated."); }
    }

    public sealed record Assertion(string Kind, string Value);
    public sealed record AssertionResult(Assertion Assertion, bool Passed);
}
