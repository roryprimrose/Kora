using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Kora.Core.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    private static readonly JsonSerializerOptions NativeSdkJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Theory]
    [InlineData("/api/version", 429)]
    [InlineData("/api/version", 500)]
    [InlineData("/api/tags", 429)]
    [InlineData("/api/tags", 500)]
    public async Task Sdk_metadata_failure_never_retries_or_sends_chat(string path, int status)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Metadata = (message, _) => Task.FromResult(Json(message,
            string.Equals(message.RequestUri!.AbsolutePath, path, StringComparison.Ordinal)
                ? """{"error":"unavailable"}""" : """{"version":"fake-runtime"}""",
            string.Equals(message.RequestUri.AbsolutePath, path, StringComparison.Ordinal) ? (HttpStatusCode)status : HttpStatusCode.OK));
        var request = f.Request();

        var reply = await f.Transport.CompleteAsync(request, Token);

        reply.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        reply.Message.Should().BeNull();
        reply.Usage.Should().BeNull();
        f.Paths.Should().Equal(path is "/api/version" ? ["/api/version"] : ["/api/version", "/api/tags"]);
        (await f.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        f.Bodies.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"version":123}""")]
    [InlineData("""{"version":"fake-runtime","version":"fake-runtime"}""")]
    public async Task Sdk_metadata_defaults_and_coercion_cannot_admit_an_invalid_runtime(string wire)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Metadata = (message, _) => Task.FromResult(Json(message, wire));

        var reply = await f.Transport.CompleteAsync(f.Request(), Token);

        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
        f.Paths.Should().Equal("/api/version");
        f.Bodies.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("unconsumed-date")]
    [InlineData("2026-10-19T20:30:00Z")]
    public async Task Sdk_catalogue_preserves_optional_uninterpreted_dates_and_exact_prefixed_digest(string modified)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Metadata = (message, _) => Task.FromResult(Json(message,
            message.RequestUri!.AbsolutePath is "/api/version" ? """{"version":"fake-runtime"}"""
                : JsonSerializer.Serialize(new
                {
                    models = new[]
                    {
                        new { name = Model, model = Model, digest = "sha256:" + Digest.ToUpperInvariant(), modified_at = modified },
                    },
                })));

        var reply = await f.Transport.CompleteAsync(f.Request(), Token);

        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        reply.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be("Genuine answer");
        f.Paths.Should().Equal("/api/version", "/api/tags", "/api/chat");
    }

    [Theory]
    [InlineData(429, false, false)]
    [InlineData(500, false, false)]
    [InlineData(429, true, false)]
    [InlineData(500, true, false)]
    [InlineData(429, false, true)]
    [InlineData(500, false, true)]
    public async Task Sdk_failure_never_retries_or_reuses_the_failed_conversation(int status, bool streaming, bool tools)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Chat = (message, _) => Task.FromResult(Json(message, """{"error":"unavailable"}""", (HttpStatusCode)status));
        var request = f.Request(toolMode: tools ? ModelToolMode.ReadOnly : ModelToolMode.None);

        var updates = 0;
        var reply = streaming
            ? await f.Transport.CompleteWithAnswerAsync(request, (_, _) =>
            {
                updates++;
                return ValueTask.CompletedTask;
            }, Token)
            : await f.Transport.CompleteAsync(request, Token);

        reply.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        reply.Message.Should().BeNull();
        reply.Usage.Should().BeNull();
        updates.Should().Be(0);
        f.Paths.Should().Equal("/api/version", "/api/tags", "/api/chat");
        (await f.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        f.Bodies.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sdk_native_tool_history_preserves_batch_order_exact_results_and_only_provider_supplied_ids(bool ids)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var batch = JsonSerializer.Serialize(new
        {
            model = Model,
            done = true,
            message = new
            {
                role = "assistant",
                content = "",
                tool_calls = Enumerable.Range(0, 2).Select(index => new
                {
                    id = ids ? $"provider_{index}" : null,
                    type = "function",
                    function = new { index, name = "application.get_version", arguments = new Dictionary<string, string>(StringComparer.Ordinal) },
                }),
            },
        }, NativeSdkJsonOptions);
        f.Chat = (message, _) => Task.FromResult(Json(message, f.Bodies.Count == 1 ? batch : Final));
        var first = await f.Transport.CompleteAsync(f.Request(), Token);
        var calls = first.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        calls.Calls.Select(call => call.CallId).Should().Equal(ids
            ? ["provider_0", "provider_1"] : ["ollama_r1_c0", "ollama_r1_c1"]);
        var results = calls.Calls.Select(call => ModelToolProtocol.CreateToolResult(call, VersionReply())).ToArray();

        var final = await f.Transport.CompleteAsync(f.Request([calls, .. results], 2), Token);

        final.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        using var sent = JsonDocument.Parse(f.Bodies[1]);
        var messages = sent.RootElement.GetProperty("messages");
        var nativeCalls = messages[2].GetProperty("tool_calls");
        nativeCalls.GetArrayLength().Should().Be(2);
        for (var index = 0; index < 2; index++)
        {
            nativeCalls[index].GetProperty("function").GetProperty("index").GetInt32().Should().Be(index);
            nativeCalls[index].TryGetProperty("id", out var callId).Should().Be(ids);
            messages[index + 3].TryGetProperty("tool_call_id", out var resultId).Should().Be(ids);
            if (ids)
            {
                callId.GetString().Should().Be($"provider_{index}");
                resultId.GetString().Should().Be($"provider_{index}");
            }
            messages[index + 3].GetProperty("tool_name").GetString().Should().Be("application.get_version");
            messages[index + 3].GetProperty("content").GetString().Should()
                .Be(System.Text.Encoding.UTF8.GetString(results[index].Result.AsSpan()));
        }
        f.Bodies.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("thinking")]
    [InlineData("wrong-model")]
    [InlineData("trailing")]
    [InlineData("unknown")]
    public async Task Buffered_sdk_decoding_cannot_normalize_a_forbidden_native_response(string fault)
    {
        await using var f = new Fixture();
        using var root = f.Root();
        var wire = fault switch
        {
            "duplicate" => Final.Replace("\"Genuine answer\"", "\"Genuine answer\",\"content\":\"second\"", StringComparison.Ordinal),
            "thinking" => Final.Replace("\"Genuine answer\"", "\"Genuine answer\",\"thinking\":\"hidden\"", StringComparison.Ordinal),
            "wrong-model" => Final.Replace(Model, "other-model", StringComparison.Ordinal),
            "trailing" => Final + "\n" + Final,
            "unknown" => Final[..^1] + ",\"authority\":true}",
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        f.Chat = (message, _) => Task.FromResult(Json(message, wire));
        var request = f.Request(toolMode: ModelToolMode.None);

        var reply = await f.Transport.CompleteAsync(request, Token);

        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
        f.Bodies.Should().ContainSingle();
        (await f.Transport.CompleteAsync(request, Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        f.Bodies.Should().ContainSingle();
    }

    [Fact]
    public async Task Buffered_sdk_preserves_exact_text_and_optional_usage_without_sending_host_identity()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        const string answer = "Line one.\r\n  \u00e9 and \u03bb.\t";
        f.Chat = (message, _) => Task.FromResult(Json(message, JsonSerializer.Serialize(new
        {
            model = Model,
            message = new { role = "assistant", content = answer },
            done = true,
            prompt_eval_count = 7,
            eval_count = 3,
        })));
        var reply = await f.Transport.CompleteAsync(f.Request(toolMode: ModelToolMode.None), Token);

        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        reply.Message.Should().BeOfType<ModelFinalResponse>().Which.Answer.Should().Be(answer);
        reply.TokenUsage.Should().Be(new ModelTokenUsage(7, 3, 7));
        using var sent = JsonDocument.Parse(f.Bodies.Should().ContainSingle().Subject);
        sent.RootElement.EnumerateObject().Select(field => field.Name).Should()
            .BeEquivalentTo("model", "messages", "stream", "think", "truncate", "shift");
        sent.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("content").GetString())
            .Should().Equal("Tools are host-mediated observations.", "Return the actual application version.");
    }
}
