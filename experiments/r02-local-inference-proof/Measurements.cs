using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Kora.Windows.Dependencies;

namespace R02Proof;

internal sealed record GenerationResult(
    string State, double? FirstTokenMs, double? FirstResponseTokenMs, double CompletionMs,
    string Response, string Thinking, JsonNode? FinalMetadata, ResourceResult Resources, string? Error,
    double? CancellationRequestedMs, double? ClientCancellationLatencyMs);

internal static class Measurements
{
    public const string Endpoint = "http://127.0.0.1:11434";

    public static async Task<JsonNode> GetAsync(HttpClient client, string path, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(Endpoint + path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
            ?? throw new InvalidDataException("Empty endpoint metadata.");
    }

    public static async Task<JsonNode> ShowAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync(Endpoint + "/api/show",
            new { model = WindowsOllamaSetupService.Model }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
            ?? throw new InvalidDataException("Empty model metadata.");
    }

    public static JsonObject StreamingPayload(string productionPayload, int context = 4096)
    {
        var payload = JsonNode.Parse(productionPayload)!.AsObject();
        payload["stream"] = true;
        payload["keep_alive"] = "5m";
        var options = payload["options"]!.AsObject();
        options["num_ctx"] = context;
        options["seed"] = 7;
        options["temperature"] = 0;
        options["num_gpu"] = 0;
        return payload;
    }

    public static async Task<JsonNode> UnloadAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        using var response = await client.PostAsJsonAsync(Endpoint + "/api/generate",
            new { model = WindowsOllamaSetupService.Model, keep_alive = 0, stream = false }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var watch = Stopwatch.StartNew();
        do
        {
            var ps = await GetAsync(client, "/api/ps", cancellationToken);
            if (ps["models"] is not JsonArray models)
                throw new InvalidDataException("Missing loaded-model inventory; unload cannot be confirmed.");
            if (!models.Any(model => model!["name"]!.GetValue<string>() == WindowsOllamaSetupService.Model))
                return ps;
            await Task.Delay(100, cancellationToken);
        } while (watch.Elapsed < TimeSpan.FromSeconds(10));
        throw new InvalidOperationException("Could not establish an unloaded-model cold start.");
    }

    public static async Task<GenerationResult> StreamAsync(
        HttpClient client, JsonObject payload, CancellationToken cancellationToken = default,
        ProcessTreeObserver? observer = null)
    {
        var watch = Stopwatch.StartNew();
        long cancellationTicks = -1;
        using var registration = cancellationToken.Register(() =>
            Interlocked.Exchange(ref cancellationTicks, watch.ElapsedTicks));
        await using var meter = new ResourceMeter(observer);
        double? first = null;
        double? firstResponse = null;
        var answer = new StringBuilder();
        var thinking = new StringBuilder();
        JsonNode? final = null;
        var state = "Unavailable";
        string? error = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/api/generate")
            {
                Content = JsonContent.Create(payload),
            };
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(deadline.Token));
            while (await reader.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length == 0) continue;
                var chunk = JsonNode.Parse(line) ?? throw new InvalidDataException("Empty streaming object.");
                if (chunk["error"] is not null)
                    throw new InvalidDataException(chunk["error"]!.GetValue<string>());
                if (chunk["model"]?.GetValue<string>() != WindowsOllamaSetupService.Model)
                    throw new InvalidDataException("Unexpected streaming model.");
                var text = chunk["response"]?.GetValue<string>() ?? string.Empty;
                var thought = chunk["thinking"]?.GetValue<string>() ?? string.Empty;
                if (text.Length > 0 || thought.Length > 0) first ??= watch.Elapsed.TotalMilliseconds;
                if (text.Length > 0) firstResponse ??= watch.Elapsed.TotalMilliseconds;
                answer.Append(text);
                thinking.Append(thought);
                if (answer.Length + thinking.Length > 128 * 1024)
                    throw new InvalidDataException("Streaming output exceeded the proof's 131072-character bound.");
                if (chunk["done"]?.GetValue<bool>() == true)
                {
                    final = chunk;
                    break;
                }
            }
            if (final is null || answer.Length == 0)
                throw new InvalidDataException("Incomplete or empty generation.");
            deadline.Token.ThrowIfCancellationRequested();
            state = "Completed";
        }
        catch (OperationCanceledException exception)
        {
            state = cancellationToken.IsCancellationRequested ? "Cancelled" : "TimedOut";
            error = exception.GetType().Name;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            or InvalidDataException or IOException or InvalidOperationException)
        {
            error = exception.GetType().Name + ": " + exception.Message;
        }
        watch.Stop();
        var requestedTicks = Interlocked.Read(ref cancellationTicks);
        double? requestedMs = requestedTicks < 0 ? null : requestedTicks * 1000.0 / Stopwatch.Frequency;
        return new(state, first, firstResponse, watch.Elapsed.TotalMilliseconds,
            answer.ToString(), thinking.ToString(), final, await meter.FinishAsync(), error,
            requestedMs, state == "Cancelled" && requestedMs.HasValue
                ? Math.Max(0, watch.Elapsed.TotalMilliseconds - requestedMs.Value) : null);
    }

    public static (string? Answer, bool AnswerOnly) ParseAnswer(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, false);
            var fields = doc.RootElement.EnumerateObject().ToArray();
            return fields.Length == 1 && fields[0].Name == "answer" && fields[0].Value.ValueKind == JsonValueKind.String
                ? (fields[0].Value.GetString(), true) : (null, false);
        }
        catch (JsonException) { return (null, false); }
    }

    public static bool CpuOnly(JsonNode ps) => ps["models"] is JsonArray models
        && models.Count == 1
        && models[0]!["name"]?.GetValue<string>() == WindowsOllamaSetupService.Model
        && models[0]!["size_vram"]?.GetValue<long>() == 0;

    public static object Distribution(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return new { count = 0 };
        double Percentile(double p) => sorted[(int)Math.Ceiling(p * sorted.Length) - 1];
        return new { count = sorted.Length, p50 = Percentile(0.5), p95 = Percentile(0.95), max = sorted[^1] };
    }
}