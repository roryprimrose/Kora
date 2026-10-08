using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kora.Core.Commands;
using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

namespace R02Proof;

internal sealed record CapturedRequest(string? Payload, string[] Calls, string? Rejection);

internal static class RequestEnvelope
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static async Task<int> RunAsync(string[] args)
    {
        var inputs = new List<string>();
        string? output = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--input" && ++i < args.Length) inputs.Add(Path.GetFullPath(args[i]));
            else if (args[i] == "--output" && output is null && ++i < args.Length) output = Path.GetFullPath(args[i]);
            else throw new ArgumentException("Usage: account-envelope --output FILE --input FILE [--input FILE ...].");
        }
        if (output is null || inputs.Count == 0 || inputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Count)
            throw new ArgumentException("A new output and unique saved evidence inputs are required.");
        if (File.Exists(output)) throw new IOException("Refusing to overwrite an existing evidence file.");
        var fixtureBytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures.json"));
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(fixtureBytes, Json)
            ?? throw new InvalidDataException("Missing fixture inventory.");
        var sources = new JsonArray();
        foreach (var input in inputs)
        {
            var bytes = await File.ReadAllBytesAsync(input);
            var source = JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidDataException("Evidence must be an object.");
            var rows = ReadSaved(source);
            sources.Add(new JsonObject
            {
                ["input"] = input,
                ["inputSha256"] = Convert.ToHexString(SHA256.HashData(bytes)),
                ["sourceMode"] = source["mode"]?.DeepClone(),
                ["requests"] = rows,
                ["coverage"] = "Only recorded payloads; missing historical ledger entries are not reconstructed.",
            });
        }
        var cases = new JsonArray();
        foreach (var fixture in fixtures)
            cases.Add(await CaptureCaseAsync(fixture.Id, fixture.Prompt, true));
        foreach (var length in new[] { 4095, 4096, 4097 })
            cases.Add(await CaptureCaseAsync("ascii-boundary-" + length, new string('x', length), length <= 4096));
        cases.Add(await CaptureCaseAsync("unicode-boundary-4096", new string('\u754c', 4096), true));
        cases.Add(await CaptureCaseAsync("surrogate-pair-boundary-4096",
            string.Concat(Enumerable.Repeat("\ud83d\ude00", 2048)), true));
        var result = new JsonObject
        {
            ["schema"] = "Kora.R02.RequestEnvelopeAccounting.v1",
            ["recordedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["scope"] = "Offline structural accounting only; captures use a synthetic HTTP handler, never a runtime.",
            ["fixtureSha256"] = Convert.ToHexString(SHA256.HashData(fixtureBytes)),
            ["proofAssemblySha256"] = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(typeof(Program).Assembly.Location))),
            ["qualificationStatus"] = "Incomplete",
            ["sourceLinkedCases"] = cases,
            ["savedInputs"] = sources,
            ["limitations"] = JsonSerializer.SerializeToNode(new[]
            {
                "UTF-16 code units and UTF-8 bytes are not tokenizer counts or context-window acceptance.",
                "Exact application JSON includes system/catalogue/status text; runtime template expansion and special tokens are unobserved.",
                "Captures use the current proof status context and no selected artifact, history or tool-result assembly; not an integrated-host maximum.",
                "num_predict is a requested generation option, not measured output/thinking tokens or a qualified reservation.",
                "No full-request/token/byte ceiling, truncation policy or effective runtime window is established.",
                "Captured requests include the proof's CPU-only override; production defaults and pins remain unchanged.",
            }),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(result.ToJsonString(Json) + Environment.NewLine));
        Console.WriteLine($"Offline envelope accounting written to {output}; token/window qualification remains open.");
        return 0;
    }

    internal static async Task<CapturedRequest> CaptureAsync(string prompt)
    {
        using var transport = new LocalTransport(new StubHandler((request, _) => Task.FromResult(
            StubHandler.Json(request.RequestUri!.AbsolutePath == "/api/tags"
                ? SelfTests.Tags : SelfTests.Decision("""{"answer":"payload capture"}""")))));
        using var client = new HttpClient(transport);
        try
        {
            await new WindowsOllamaReasoner(client, new BuiltInCommandCatalog())
                .ReasonAsync(prompt, SelfTests.Context, null, CancellationToken.None);
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "request")
        {
            SelfTests.Require(transport.Calls.Count == 0, "Rejected user input reached the synthetic transport.");
            return new(null, transport.Calls.ToArray(), exception.Message);
        }
        return new(transport.GenerationPayloads.Single(), transport.Calls.ToArray(), null);
    }

    internal static async Task<JsonObject> CaptureCaseAsync(string id, string prompt, bool expectedAccepted)
    {
        var capture = await CaptureAsync(prompt);
        SelfTests.Require((capture.Payload is not null) == expectedAccepted, "Source-linked input boundary changed.");
        return new JsonObject
        {
            ["case"] = id,
            ["userInputUtf16CodeUnits"] = prompt.Length,
            ["userInputUtf8Bytes"] = Encoding.UTF8.GetByteCount(prompt),
            ["outcome"] = capture.Payload is null ? "Rejected before transport" : "Captured; no inference",
            ["calls"] = JsonSerializer.SerializeToNode(capture.Calls),
            ["rejection"] = capture.Rejection,
            ["envelope"] = capture.Payload is { } payload ? Account(payload, id, true) : null,
        };
    }

    internal static JsonArray ReadSaved(JsonObject source)
    {
        if (source["schema"]?.GetValue<string>() != "Kora.R02.LocalInferenceProof.v1")
            throw new InvalidDataException("Unsupported saved inference evidence schema.");
        var rows = new JsonArray();
        if (source["experimentRequests"] is JsonArray ledger)
        {
            for (var i = 0; i < ledger.Count; i++)
                rows.Add(Account(ledger[i]?.GetValue<string>() ?? throw new InvalidDataException("Missing request payload."),
                    $"experimentRequests[{i}]", true));
        }
        else if (source["experimentRequests"] is not null)
            throw new InvalidDataException("Request ledger must be an array.");
        else
        {
            foreach (var path in new[] { "samplingWarmups", "samplingTrials", "observerControlTrials", "streamTrials" })
            {
                if (source[path] is null) continue;
                if (source[path] is not JsonArray trials) throw new InvalidDataException("Trial collection must be an array.");
                for (var i = 0; i < trials.Count; i++)
                {
                    if (trials[i]?["request"] is not JsonObject request)
                        throw new InvalidDataException("Missing recorded trial request.");
                    rows.Add(Account(request.ToJsonString(), $"{path}[{i}].request", false));
                }
            }
        }
        if (rows.Count == 0) throw new InvalidDataException("No supported recorded request payloads.");
        return rows;
    }

    internal static JsonObject Account(string payload, string reference, bool recordedString)
    {
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Request payload must be an object.");
        var properties = doc.RootElement.EnumerateObject().ToArray();
        if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length
            || !doc.RootElement.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(model.GetString()))
            throw new InvalidDataException("Request requires unique fields and a named model.");
        var fields = new JsonArray();
        var valueBytes = 0;
        foreach (var property in properties)
        {
            var bytes = Encoding.UTF8.GetByteCount(property.Value.GetRawText());
            valueBytes = checked(valueBytes + bytes);
            var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            fields.Add(new JsonObject
            {
                ["name"] = property.Name,
                ["kind"] = property.Value.ValueKind.ToString(),
                ["serializedValueUtf8Bytes"] = bytes,
                ["decodedStringUtf16CodeUnits"] = text?.Length,
                ["decodedStringUtf8Bytes"] = text is null ? null : Encoding.UTF8.GetByteCount(text),
            });
        }
        var total = Encoding.UTF8.GetByteCount(payload);
        return new JsonObject
        {
            ["requestReference"] = reference,
            ["representation"] = recordedString ? "Recorded payload string; exact UTF-8 re-encoding"
                : "Recorded object reserialized; original byte framing is unestablished",
            ["payloadSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            ["serializedJsonUtf16CodeUnits"] = payload.Length,
            ["serializedUtf8Bytes"] = total,
            ["objectNamesSeparatorsWhitespaceUtf8Bytes"] = checked(total - valueBytes),
            ["fields"] = fields,
            ["modelTokens"] = null,
            ["expandedTemplateTokens"] = null,
        };
    }
}
