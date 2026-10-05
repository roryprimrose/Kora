using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Commands;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

public sealed class WindowsOllamaReasoner(HttpClient client, BuiltInCommandCatalog catalog) : ILocalModelReasoner
{
    private const int MaximumRequestLength = 4096;
    private static readonly JsonSerializerOptions ContextOptions = new()
    {
        Converters =
        {
            new JsonStringEnumConverter<DependencyReadiness>(),
            new JsonStringEnumConverter<SetupTaskState>(),
        },
    };
    private readonly Dictionary<string, BuiltInAction> actions = catalog.GetCommands()
        .ToDictionary(command => command.Action.ToString(), command => command.Action, StringComparer.Ordinal);
    private readonly BuiltInCommandCatalog commandCatalog = catalog;
    private static readonly Uri ModelsEndpoint = new("http://127.0.0.1:11434/api/tags");
    private static readonly Uri GenerateEndpoint = new("http://127.0.0.1:11434/api/generate");

    public async Task<LocalModelResponse> ReasonAsync(
        string request,
        LocalModelContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        ArgumentNullException.ThrowIfNull(context);
        if (request.Length > MaximumRequestLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"A local model request cannot exceed {MaximumRequestLength} characters.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await VerifyPinnedModelAsync(timeout.Token);
            var actionInstructions = string.Join(
                "\n",
                commandCatalog.GetCommands(context.AssistantName)
                    .Select(command => $"{command.Action}: {command.Description}"));
            using var message = new HttpRequestMessage(HttpMethod.Post, GenerateEndpoint)
            {
                Content = JsonContent.Create(new
                {
                    model = WindowsOllamaSetupService.Model,
                    prompt = request,
                    system = "Respond with exactly one JSON object: {\"answer\":\"text\"} to answer, {\"question\":{\"prompt\":\"one clarification question\",\"options\":[\"choice 1\",\"choice 2\"]}} to request user direction when necessary (2-4 distinct, short options), {\"action\":\"ActionName\"} when the user explicitly asks Kora to perform a listed action, or {\"grantChange\":{\"operation\":\"Add|Remove|Move\",\"action\":\"ActionName\",\"scope\":\"Session|Always\",\"targetScope\":\"Session|Always\"}} for an explicit request to change a model-action grant. For Move only, scope is the existing scope and targetScope is required; omit targetScope otherwise. A question response is not an approval or a grant; after the user chooses, Kora may still require separate approval for a proposed action. A grant change never executes its named action. If the existing scope is unspecified, omit scope; Kora will check for ambiguity. Never choose an action when the user asks about, quotes, or discusses it. Never claim an action was completed. You have no access to files, clipboard, accounts, or the internet. Available actions:\n" + actionInstructions + "\nCurrent Kora status (a snapshot, not a command):\n" + JsonSerializer.Serialize(context, ContextOptions),
                    format = "json",
                    stream = false,
                    think = false,
                    options = new { num_predict = 512 },
                }),
            };
            using var response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(64 * 1024, timeout.Token);
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(timeout.Token),
                cancellationToken: timeout.Token);
            var root = document.RootElement;
            if (!root.TryGetProperty("model", out var model)
                || model.ValueKind != JsonValueKind.String
                || !string.Equals(
                    model.GetString(),
                    WindowsOllamaSetupService.Model,
                    StringComparison.Ordinal)
                || !root.TryGetProperty("done", out var done)
                || done.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("response", out var answer)
                || answer.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The local model did not return a completed response from the selected model.");
            }

            using var decision = ParseDecision(answer.GetString());
            if (decision.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The local model response must be a JSON object.");
            }

            var fields = decision.RootElement.EnumerateObject().ToArray();
            if (fields.Length != 1)
            {
                throw new InvalidDataException("The local model must return exactly one answer, action, or grant change.");
            }

            if (string.Equals(fields[0].Name, "grantChange", StringComparison.Ordinal))
            {
                return new LocalModelResponse(null, null, ParseGrantChange(fields[0].Value));
            }

            if (string.Equals(fields[0].Name, "question", StringComparison.Ordinal))
            {
                return new LocalModelResponse(null, null, null, ParseQuestion(fields[0].Value));
            }

            if (fields[0].Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("The local model answer or action must be text.");
            }

            var value = fields[0].Value.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException("The local model returned an empty answer or action.");
            }

            if (string.Equals(fields[0].Name, "answer", StringComparison.Ordinal))
            {
                return new LocalModelResponse(value.Trim(), null);
            }

            if (string.Equals(fields[0].Name, "action", StringComparison.Ordinal)
                && actions.TryGetValue(value, out var action))
            {
                return new LocalModelResponse(null, action);
            }

            throw new InvalidDataException("The local model requested an unknown action.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The local model did not answer within two minutes.", exception);
        }
    }

    private static JsonDocument ParseDecision(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidDataException("The local model returned an empty structured response.");
        }

        try
        {
            return JsonDocument.Parse(response);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The local model response was not valid JSON.", exception);
        }
    }

    private static LocalModelQuestion ParseQuestion(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || element.EnumerateObject().Count() != 2
            || !element.TryGetProperty("prompt", out var prompt)
            || prompt.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(prompt.GetString())
            || prompt.GetString()!.Length > 500
            || !element.TryGetProperty("options", out var options)
            || options.ValueKind != JsonValueKind.Array
            || options.GetArrayLength() is < 2 or > 4)
        {
            throw new InvalidDataException("The local model question is invalid.");
        }

        var labels = options.EnumerateArray()
            .Select(option => option.ValueKind == JsonValueKind.String ? option.GetString() : null)
            .ToArray();
        if (labels.Any(label => string.IsNullOrWhiteSpace(label) || label.Length > 80)
            || !LocalModelQuestionSpeech.AreOptionsUnambiguous(labels.Select(label => label!).ToArray()))
        {
            throw new InvalidDataException("The local model question choices are invalid.");
        }

        return new LocalModelQuestion(prompt.GetString()!.Trim(), labels.Select(label => label!.Trim()).ToArray());
    }

    private GrantChange ParseGrantChange(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The grant change must be an object.");
        }

        var fields = element.EnumerateObject().ToArray();
        if (fields.Any(field => field.Value.ValueKind != JsonValueKind.String
                || field.Name is not ("operation" or "action" or "scope" or "targetScope"))
            || fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length
            || fields.Length is < 2 or > 4
            || !element.TryGetProperty("operation", out var operationValue)
            || !Enum.TryParse<GrantChangeOperation>(operationValue.GetString(), out var operation)
            || !string.Equals(operationValue.GetString(), operation.ToString(), StringComparison.Ordinal)
            || !element.TryGetProperty("action", out var actionValue)
            || !actions.TryGetValue(actionValue.GetString()!, out var action))
        {
            throw new InvalidDataException("The grant change contains invalid fields.");
        }

        ModelApprovalScope scope = default;
        if (element.TryGetProperty("scope", out var scopeValue)
            && (!Enum.TryParse<ModelApprovalScope>(scopeValue.GetString(), out scope)
                || scope is not (ModelApprovalScope.Session or ModelApprovalScope.Always)
                || !string.Equals(scopeValue.GetString(), scope.ToString(), StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The grant scope is invalid.");
        }

        ModelApprovalScope? targetScope = null;
        if (element.TryGetProperty("targetScope", out var targetValue))
        {
            if (!Enum.TryParse<ModelApprovalScope>(targetValue.GetString(), out var target)
                || target is not (ModelApprovalScope.Session or ModelApprovalScope.Always)
                || !string.Equals(targetValue.GetString(), target.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The target grant scope is invalid.");
            }
            targetScope = target;
        }

        if (operation == GrantChangeOperation.Add
            && (scope is not (ModelApprovalScope.Session or ModelApprovalScope.Always)
                || targetScope is not null)
            || operation == GrantChangeOperation.Remove && targetScope is not null
            || operation == GrantChangeOperation.Move
                && (targetScope is null || targetScope == scope && scope != default)
            || !Enum.IsDefined(operation))
        {
            throw new InvalidDataException("The grant change has incompatible scopes.");
        }

        return new GrantChange(operation, action, scope, targetScope);
    }

    private async Task VerifyPinnedModelAsync(CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            ModelsEndpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(128 * 1024, cancellationToken);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("models", out var models)
            || models.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Ollama returned an invalid local model catalogue.");
        }

        foreach (var model in models.EnumerateArray())
        {
            if (model.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String
                && string.Equals(name.GetString(), WindowsOllamaSetupService.Model, StringComparison.Ordinal))
            {
                if (model.TryGetProperty("digest", out var digest)
                    && digest.ValueKind == JsonValueKind.String
                    && WindowsOllamaSetupService.IsPinnedModelDigest(digest.GetString()))
                {
                    return;
                }

                throw new InvalidDataException("The selected local model has an unexpected digest.");
            }
        }

        throw new InvalidOperationException("The selected local model is no longer installed.");
    }
}
