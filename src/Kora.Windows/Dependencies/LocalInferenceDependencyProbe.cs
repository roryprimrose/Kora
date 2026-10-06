using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

public sealed class LocalInferenceDependencyProbe(HttpClient client) : ISetupDependencyProbe
{
    public string TaskId => "local.inference";

    public string TaskName => "Local model inference (Ollama)";

    private static readonly Uri VersionEndpoint = new("http://127.0.0.1:11434/api/version");
    private static readonly Uri ModelsEndpoint = new("http://127.0.0.1:11434/api/tags");
    private static readonly Uri GenerateEndpoint = new("http://127.0.0.1:11434/api/generate");

    public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var version = await client.GetAsync(VersionEndpoint, timeout.Token);
            if (version.StatusCode != HttpStatusCode.OK)
            {
                return Status(DependencyReadiness.Failed,
                    $"The local Ollama endpoint returned HTTP {(int)version.StatusCode}; no model was activated.");
            }

            using var versionDocument = await JsonDocument.ParseAsync(
                await version.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            if (versionDocument.RootElement.ValueKind != JsonValueKind.Object
                || !versionDocument.RootElement.TryGetProperty("version", out var versionValue)
                || versionValue.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(versionValue.GetString()))
            {
                return Status(DependencyReadiness.Incompatible, "The local endpoint did not identify a compatible Ollama runtime.");
            }

            using var models = await client.GetAsync(ModelsEndpoint, timeout.Token);
            if (models.StatusCode != HttpStatusCode.OK)
            {
                return Status(DependencyReadiness.Failed,
                    $"Ollama {versionValue.GetString()} responded, but model discovery returned HTTP {(int)models.StatusCode}.");
            }

            using var modelDocument = await JsonDocument.ParseAsync(
                await models.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            if (modelDocument.RootElement.ValueKind != JsonValueKind.Object
                || !modelDocument.RootElement.TryGetProperty("models", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return Status(DependencyReadiness.Incompatible, "Ollama returned an invalid model catalogue.");
            }

            if (list.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Object))
            {
                return Status(DependencyReadiness.Incompatible, "Ollama returned an invalid model catalogue.");
            }

            var pinned = list.EnumerateArray().FirstOrDefault(item =>
                item.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && string.Equals(name.GetString(), WindowsOllamaSetupService.Model, StringComparison.Ordinal));
            if (pinned.ValueKind != JsonValueKind.Undefined)
            {
                if (!pinned.TryGetProperty("digest", out var digest)
                    || digest.ValueKind != JsonValueKind.String
                    || !WindowsOllamaSetupService.IsPinnedModelDigest(digest.GetString()))
                {
                    return Status(DependencyReadiness.Incompatible,
                        $"The installed {WindowsOllamaSetupService.Model} has an unexpected digest. Kora will not replace it automatically.");
                }

                using var inferenceTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                inferenceTimeout.CancelAfter(TimeSpan.FromSeconds(90));
                using var request = new HttpRequestMessage(HttpMethod.Post, GenerateEndpoint)
                {
                    Content = JsonContent.Create(new
                    {
                        model = WindowsOllamaSetupService.Model,
                        prompt = "Respond with OK.",
                        stream = false,
                    }),
                };
                using var inference = await client.SendAsync(request, inferenceTimeout.Token);
                if (!inference.IsSuccessStatusCode)
                {
                    return Status(DependencyReadiness.Failed,
                        $"The pinned local model returned HTTP {(int)inference.StatusCode} during its inference check.");
                }

                using var result = await JsonDocument.ParseAsync(
                    await inference.Content.ReadAsStreamAsync(inferenceTimeout.Token),
                    cancellationToken: inferenceTimeout.Token);
                if (result.RootElement.ValueKind != JsonValueKind.Object
                    || !result.RootElement.TryGetProperty("done", out var done)
                    || done.ValueKind != JsonValueKind.True
                    || !result.RootElement.TryGetProperty("response", out var answer)
                    || answer.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(answer.GetString()))
                {
                    return Status(DependencyReadiness.Failed,
                        "The pinned local model did not complete its inference check.");
                }

                return Status(DependencyReadiness.Ready,
                    $"{WindowsOllamaSetupService.Model} passed its local inference check. Unmatched requests can be answered locally; built-in commands remain deterministic.");
            }

            return Status(DependencyReadiness.NeedsConfiguration,
                list.GetArrayLength() == 0
                    ? $"Ollama {versionValue.GetString()} is responding locally, but the selected {WindowsOllamaSetupService.Model} model is missing."
                    : $"Ollama {versionValue.GetString()} and {list.GetArrayLength()} other model(s) were detected. The selected {WindowsOllamaSetupService.Model} model is missing.");
        }
        catch (HttpRequestException exception) when (exception.InnerException is SocketException
        { SocketErrorCode: SocketError.ConnectionRefused })
        {
            return Status(DependencyReadiness.Missing,
                "No Ollama runtime is listening on 127.0.0.1:11434. Installation requires approval after Kora has a supported local-model adapter.");
        }
        catch (HttpRequestException exception)
        {
            return Status(DependencyReadiness.Failed, $"The local Ollama endpoint could not be checked: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Status(DependencyReadiness.Failed, "The local Ollama endpoint or model inference check timed out.");
        }
        catch (JsonException exception)
        {
            return Status(DependencyReadiness.Incompatible, $"The local endpoint returned invalid Ollama metadata: {exception.Message}");
        }
    }

    private static DependencyStatus Status(DependencyReadiness readiness, string detail) =>
        new("local.inference", "Local model inference (Ollama)", readiness, detail);
}