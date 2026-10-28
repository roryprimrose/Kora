using System.Text.Json;

namespace Kora.Application.Definitions;

internal static class DefinitionBodyProposal
{
    internal static string Parse(string answer)
    {
        try
        {
            using var json = JsonDocument.Parse(answer, new JsonDocumentOptions { MaxDepth = 2 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1
                || !root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.String)
            { throw new InvalidDataException("The model must return one complete body string. No output was stripped or truncated."); }
            return body.GetString()!;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        { throw new InvalidDataException("Invalid refinement JSON. No output was stripped or truncated; draft and critique retained.", exception); }
    }
}
