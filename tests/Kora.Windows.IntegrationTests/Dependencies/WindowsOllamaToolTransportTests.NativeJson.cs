using System.Net;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"tool","content":"answer"},"done":true}""")]
    [InlineData("""{"model":"other","message":{"role":"assistant","content":"answer"},"done":true}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"answer"},"done":false}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":""},"done":true}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"answer","authority":true},"done":true}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"a","content":"b"},"done":true}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"\ud800"},"done":true}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"\u001b"},"done":true}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"answer"},"done":true,"done_reason":"length"}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"answer","thinking":"hidden"},"done":true}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"answer"},"done":true,"eval_count":"1"}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"answer"},"done":true,"session":"forged"}""")]
    [InlineData("""{"model":"deterministic-fake:one","message":{"role":"assistant","content":"answer"},"done":true}{}""")]
    public async Task Native_malformed_unknown_duplicate_incomplete_or_hostile_output_is_terminal(string json)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message, json));
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
        fixture.Bodies.Should().ContainSingle();
    }

    [Theory]
    [InlineData("""{"name":"shell.run","arguments":{}}""")]
    [InlineData("""{"name":"application.get_version","arguments":{"command":"unsafe"}}""")]
    [InlineData("""{"name":"application.get_version","arguments":"{}"}""")]
    [InlineData("""{"name":"application.get_version","arguments":{},"index":1}""")]
    [InlineData("""{"name":"capabilities.list","arguments":{"count":1,"count":2}}""")]
    [InlineData("""{"name":"runtime.get_status","arguments":{"id":"other"}}""")]
    [InlineData("""{"name":"application.get_version","arguments":{},"uri":"https://hostile"}""")]
    public async Task Native_call_validation_uses_authoritative_Core_admission_before_a_call_is_returned(string function)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message,
            $$"""{"model":"{{Model}}","message":{"role":"assistant","content":"","tool_calls":[{"function":{{function}}}]},"done":true}"""));
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
    }

    [Fact]
    public async Task Whole_message_rejection_precedes_any_host_dispatch_even_when_the_first_call_is_valid()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message,
            $$"""{"model":"{{Model}}","message":{"role":"assistant","content":"","tool_calls":[{"id":"call_1","function":{"name":"application.get_version","arguments":{}}},{"id":"call_1","function":{"name":"application.get_version","arguments":{}}}]},"done":true}"""));
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Message.Should().BeNull();
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
    }

    [Theory]
    [InlineData(0, ModelTurnOutcome.Succeeded)]
    [InlineData(1, ModelTurnOutcome.Denied)]
    public async Task Complete_raw_UTF8_response_limit_is_exact_not_a_character_or_truncation_proxy(int extra, ModelTurnOutcome expected)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var json = Final.Replace("Genuine answer", "Genuine \u00e9 answer", StringComparison.Ordinal);
        var padding = ModelToolProtocol.MaximumOutputUtf8Bytes - Encoding.UTF8.GetByteCount(json) + extra;
        var text = json + new string(' ', padding);
        Encoding.UTF8.GetByteCount(text).Should().Be(ModelToolProtocol.MaximumOutputUtf8Bytes + extra);
        fixture.Chat = (message, _) => Task.FromResult(Json(message, text));
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(expected);
        if (extra != 0) { reply.Message.Should().BeNull(); }
    }

    [Fact]
    public async Task Native_request_framing_and_host_selected_model_name_are_included_in_complete_input_bound()
    {
        await using var fixture = new Fixture { BoundModel = new string('m', ModelToolProtocol.MaximumInputUtf8Bytes) };
        using var root = fixture.Root();
        (await fixture.Transport.CompleteAsync(fixture.Request(), Token)).Outcome.Should().Be(ModelTurnOutcome.Denied);
        fixture.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_call_ID_in_a_later_round_is_not_a_new_invocation()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message,
            Call.Replace("""{"function":""", """{"id":"same_call","function":""", StringComparison.Ordinal)));
        var first = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        var calls = first.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0], new(CapabilityOutcome.Succeeded, "observed"));
        var repeated = await fixture.Transport.CompleteAsync(fixture.Request([calls, result], 2), Token);
        repeated.Outcome.Should().Be(ModelTurnOutcome.Denied);
        repeated.Message.Should().BeNull();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("digest")]
    [InlineData("catalogue-duplicate")]
    [InlineData("catalogue-extra")]
    public async Task Metadata_drift_or_ambiguous_model_source_never_receives_private_chat_text(string failure)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Metadata = (message, _) => Task.FromResult(Json(message,
            string.Equals(message.RequestUri!.AbsolutePath, "/api/version", StringComparison.Ordinal)
                ? string.Equals(failure, "version", StringComparison.Ordinal) ? """{"version":"changed"}""" : """{"version":"fake-runtime"}"""
                : failure switch
                {
                    "digest" => $$"""{"models":[{"name":"{{Model}}","digest":"{{new string('b', 64)}}"}]}""",
                    "catalogue-duplicate" => $$"""{"models":[{"name":"{{Model}}","digest":"{{Digest}}"},{"name":"{{Model}}","digest":"{{Digest}}"}]}""",
                    _ => $$"""{"models":[{"name":"{{Model}}","digest":"{{Digest}}","authority":true}]}""",
                }));
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
        fixture.Bodies.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ModelTurnOutcome.Unavailable)]
    [InlineData(HttpStatusCode.TemporaryRedirect, ModelTurnOutcome.DeniedEgress)]
    public async Task HTTP_failure_is_terminal_without_redirect_or_retry(HttpStatusCode status, ModelTurnOutcome outcome)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) =>
        {
            var response = Json(message, "{}", status);
            response.Headers.Location = new("http://untrusted.invalid/");
            return Task.FromResult(response);
        };
        (await fixture.Transport.CompleteAsync(fixture.Request(), Token)).Outcome.Should().Be(outcome);
        fixture.Bodies.Should().ContainSingle();
    }

    [Fact]
    public async Task Malformed_UTF8_is_not_replacement_decoded_into_an_answer()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) =>
        {
            var response = Json(message, "{}");
            response.Content.Dispose();
            response.Content = new ByteArrayContent([0xC3, 0x28]);
            response.Content.Headers.ContentType = new("application/json");
            return Task.FromResult(response);
        };
        var reply = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
    }

    [Fact]
    public async Task Cumulative_native_requests_are_bounded_independently_of_each_individual_request()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) => Task.FromResult(Json(message, Call));
        var user = new string('u', 10_000);
        var first = await fixture.Transport.CompleteAsync(fixture.Request(user: user), Token);
        var calls1 = first.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        var result1 = ModelToolProtocol.CreateToolResult(calls1.Calls[0], new(CapabilityOutcome.Succeeded, "observed"));
        var second = await fixture.Transport.CompleteAsync(fixture.Request([calls1, result1], 2, user: user), Token);
        var calls2 = second.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        var result2 = ModelToolProtocol.CreateToolResult(calls2.Calls[0], new(CapabilityOutcome.Succeeded, "observed"));
        var third = await fixture.Transport.CompleteAsync(fixture.Request([calls1, result1, calls2, result2], 3, user: user), Token);
        third.Outcome.Should().Be(ModelTurnOutcome.Denied);
        third.Message.Should().BeNull();
        fixture.Bodies.Should().HaveCount(2);
        fixture.Bodies.Sum(body => body.Length).Should().BeLessThanOrEqualTo(ModelToolProtocol.MaximumInputUtf8Bytes);
    }

    [Fact]
    public async Task Cumulative_native_assistant_and_tool_result_output_never_truncates_into_a_final_answer()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        fixture.Chat = (message, _) =>
        {
            var text = fixture.Bodies.Count == 1 ? Call : Final;
            return Task.FromResult(Json(message, text.PadRight(2300, ' ')));
        };
        var first = await fixture.Transport.CompleteAsync(fixture.Request(), Token);
        var calls = first.Message.Should().BeOfType<ModelAssistantToolCalls>().Subject;
        var result = ModelToolProtocol.CreateToolResult(calls.Calls[0], new(CapabilityOutcome.Succeeded, "observed"));
        var reply = await fixture.Transport.CompleteAsync(fixture.Request([calls, result], 2), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
    }
}
