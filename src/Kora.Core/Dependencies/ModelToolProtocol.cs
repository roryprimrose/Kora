using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

/// <summary>Authoritative provider-neutral wire policy. Invalid data is rejected whole, never repaired.</summary>
public static partial class ModelToolProtocol
{
    public const int SchemaVersion = 1;
    public static int MaximumCalls => ReadOnlyCapabilityCatalog.MaximumRecords;
    public static int MaximumRounds => MaximumCalls + 1;
    public static int MaximumInputUtf8Bytes => ModelContextEnvelope.MaximumInputUtf8Bytes;
    public static int MaximumOutputUtf8Bytes => ModelContextEnvelope.MaximumOutputUtf8Bytes;
    public static TimeSpan Deadline { get; } = TimeSpan.FromSeconds(15);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static ModelToolRequest CreateRequest(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ModelToolContext context, IEnumerable<ModelToolMessage> messages, int round, ModelToolBudget? budget = null,
        bool clarificationAllowed = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(messages);
        ValidateProvenance(provenance);
        if (context.Provenance != provenance)
        { throw new InvalidDataException("A context cannot be reused by another host turn."); }
        if (context.ToolMode is ModelToolMode.LocalWebPage or ModelToolMode.McpReadOnly or ModelToolMode.SessionDiscovery
            && provenance.Selection.Provider != ModelProviderIdentity.Ollama)
        { throw new InvalidDataException("The approved page and its results may only enter the original local model turn."); }
        if (profile is not null)
        {
            ProviderProfileRules.ValidateTuple(profile);
            if (profile.Service != ProviderProfileRules.ServiceFor(provenance.Selection.Provider))
            { throw new InvalidDataException("The provider tuple does not belong to the host selection."); }
        }
        var history = messages.Take(MaximumCalls * 2 + 1).ToImmutableArray();
        if (clarificationAllowed && context.ToolMode != ModelToolMode.None)
        { throw new InvalidDataException("Clarification turns are tool-free."); }
        if (context.ToolMode == ModelToolMode.None && !clarificationAllowed && !history.IsEmpty)
        { throw new InvalidDataException("A tool-free turn cannot contain any tool history."); }
        if (clarificationAllowed) { ValidateQuestionHistory(history); }
        else { ValidateHistory(history); }
        var expectedRound = history.Count(message => message is ModelAssistantMessage) + 1;
        if (round != expectedRound || round > (context.Constraints?.MaximumRounds ?? MaximumRounds)
            || history.OfType<ModelAssistantToolCalls>().Sum(item => item.Calls.Length) > (context.Constraints?.MaximumCalls ?? MaximumCalls)
            || history.OfType<ModelAssistantToolCalls>().Any(item => item.Calls.Any(call =>
                context.McpTool is { } external
                    ? !string.Equals(external.Id, call.CapabilityId, StringComparison.Ordinal)
                    : !context.Tools.Any(tool => string.Equals(tool.Id, call.CapabilityId, StringComparison.Ordinal)))))
        { throw new InvalidDataException("The continuation round is not the complete serial history."); }
        // Revalidate the immutable source against this request rather than allowing cross-session reuse.
        ReadContext(context.Wire.AsSpan(), provenance);
        var request = new ModelToolRequest(provenance, profile, context, history, round,
            budget ?? new(context.Constraints?.InputUtf8Bytes ?? MaximumInputUtf8Bytes,
                context.Constraints?.OutputUtf8Bytes ?? MaximumOutputUtf8Bytes), clarificationAllowed);
        if (request.Budget.InputUtf8Bytes > (context.Constraints?.InputUtf8Bytes ?? MaximumInputUtf8Bytes)
            || request.Budget.OutputUtf8Bytes > (context.Constraints?.OutputUtf8Bytes ?? MaximumOutputUtf8Bytes))
        { throw new InvalidDataException("The transport budget exceeds the exact turn constraints."); }
        if (SerializeRequest(request).Length > request.Budget.InputUtf8Bytes)
        { throw new InvalidDataException("The complete model request exceeds the input limit."); }
        return request;
    }

    public static byte[] SerializeRequest(ModelToolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", request.ClarificationAllowed ? ClarificationSchemaVersion : SchemaVersion);
            writer.WritePropertyName("context");
            writer.WriteRawValue(request.Context.Wire.AsSpan());
            writer.WriteStartArray("messages");
            foreach (var message in request.Messages) { WriteMessage(writer, message); }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static void ValidateProvenance(ModelTurnProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        provenance.Turn.Validate();
        if (provenance.Request is null || provenance.Selection is not { IsValid: true })
        { throw new InvalidDataException("A model turn requires complete host provenance."); }
    }

    internal static JsonDocument ReadJson(ReadOnlySpan<byte> wire, int limit)
    {
        var document = ReadJson(wire, limit, out var duplicateFields);
        if (duplicateFields)
        {
            document.Dispose();
            throw new InvalidDataException("Duplicate JSON fields are not admitted.");
        }
        return document;
    }

    internal static JsonDocument ReadJson(ReadOnlySpan<byte> wire, int limit, out bool duplicateFields,
        int maximumDepth = 16)
    {
        duplicateFields = false;
        if (wire.IsEmpty || wire.Length > limit)
        { throw new InvalidDataException("The complete JSON message is empty or exceeds its byte limit."); }
        try
        {
            var text = StrictUtf8.GetString(wire);
            var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = maximumDepth });
            try { ValidateElement(document.RootElement, ref duplicateFields); }
            catch { document.Dispose(); throw; }
            return document;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException)
        { throw new InvalidDataException("The complete JSON message is malformed.", exception); }
    }

    private static void ValidateElement(JsonElement element, ref bool duplicateFields)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                ValidateText(property.Name);
                if (!names.Add(property.Name)) { duplicateFields = true; }
                ValidateElement(property.Value, ref duplicateFields);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) { ValidateElement(item, ref duplicateFields); }
        }
        else if (element.ValueKind == JsonValueKind.String) { ValidateText(element.GetString()!); }
    }

    private static void ValidateText(string text)
    {
        try { StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Malformed Unicode is not admitted.", exception); }
        if (text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        { throw new InvalidDataException("Control characters are not admitted."); }
    }

    private static void RequireFields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Count() != names.Length
            || names.Any(name => !element.TryGetProperty(name, out _)))
        { throw new InvalidDataException("The message has missing or unknown fields."); }
    }

    private static string ReadString(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        { throw new InvalidDataException("A required string field has the wrong type."); }
        return value.GetString()!;
    }
}
