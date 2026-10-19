using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Theory]
    [InlineData(0, ModelTurnOutcome.Succeeded)]
    [InlineData(-1, ModelTurnOutcome.Denied)]
    public async Task Remaining_native_input_budget_is_enforced_at_the_exact_complete_frame_boundary_before_HTTP(
        int adjustment, ModelTurnOutcome expected)
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var initial = fixture.Request();
        var canonicalBytes = ModelToolProtocol.SerializeRequest(initial).Length;
        var originalNativeBytes = OllamaToolWire.WriteRequest(initial, Model, []).Length;
        fixture.BoundModel = Model + new string('m', Math.Max(1, canonicalBytes - originalNativeBytes + 1));
        var nativeBytes = OllamaToolWire.WriteRequest(initial, fixture.BoundModel, []).Length;
        nativeBytes.Should().BeGreaterThan(canonicalBytes);
        var answer = NativeAnswer(fixture.BoundModel);
        Encoding.UTF8.GetByteCount(answer).Should().BeLessThan(ModelToolProtocol.MaximumOutputUtf8Bytes);
        fixture.Chat = (message, _) => Task.FromResult(Json(message, answer));
        var request = fixture.Request(budget: new(nativeBytes + adjustment, ModelToolProtocol.MaximumOutputUtf8Bytes));
        var reply = await fixture.Transport.CompleteAsync(request, Token);
        reply.Outcome.Should().Be(expected);
        reply.Provenance.Should().BeSameAs(request.Provenance);
        if (expected == ModelTurnOutcome.Succeeded)
        {
            fixture.Bodies.Should().ContainSingle().Which.Length.Should().Be(nativeBytes);
            reply.Usage.Should().Be(new ModelToolUsage(nativeBytes,
                Math.Max(Encoding.UTF8.GetByteCount(answer), ModelToolProtocol.SerializeMessage(reply.Message!).Length)));
        }
        else
        {
            fixture.Paths.Should().BeEmpty();
            reply.Message.Should().BeNull();
            reply.Usage.Should().BeNull();
        }
    }

    [Fact]
    public async Task Canonical_input_larger_than_native_is_charged_without_disclosing_host_evidence_identity()
    {
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var evidence = new ModelContextEvidence(new(Guid.NewGuid()), fixture.RequestIdentity, new(1),
            "Recorded read-only observation.", ModelEvidenceDisclosure.LocalOnly);
        var request = fixture.Request(evidence: [evidence]);
        var reply = await fixture.Transport.CompleteAsync(request, Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        reply.Provenance.Should().BeSameAs(request.Provenance);
        var canonicalBytes = ModelToolProtocol.SerializeRequest(request).Length;
        canonicalBytes.Should().BeGreaterThan(fixture.Bodies.Single().Length);
        reply.Usage.Should().NotBeNull();
        var usage = reply.Usage!;
        usage.InputUtf8Bytes.Should().Be(canonicalBytes);
        usage.OutputUtf8Bytes.Should().Be(Math.Max(Encoding.UTF8.GetByteCount(Final),
            ModelToolProtocol.SerializeMessage(reply.Message!).Length));
        var sent = Encoding.UTF8.GetString(fixture.Bodies.Single());
        sent.Should().NotContain(evidence.Id.Value.ToString("D"));
        sent.Should().NotContain(fixture.RequestIdentity.SessionId.Value.ToString("D"));
    }

    [Theory]
    [InlineData(0, ModelTurnOutcome.Succeeded)]
    [InlineData(1, ModelTurnOutcome.Denied)]
    public async Task Remaining_output_budget_bounds_the_complete_raw_response_not_the_global_maximum(
        int extra, ModelTurnOutcome expected)
    {
        const int remaining = 512;
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var text = Final.PadRight(remaining + extra, ' ');
        Encoding.UTF8.GetByteCount(text).Should().Be(remaining + extra);
        fixture.Chat = (message, _) => Task.FromResult(Json(message, text));
        var reply = await fixture.Transport.CompleteAsync(
            fixture.Request(budget: new(ModelToolProtocol.MaximumInputUtf8Bytes, remaining)), Token);
        reply.Outcome.Should().Be(expected);
        if (expected == ModelTurnOutcome.Succeeded)
        {
            reply.Usage.Should().NotBeNull();
            reply.Usage!.OutputUtf8Bytes.Should().Be(remaining);
        }
        else
        {
            reply.Message.Should().BeNull();
            reply.Usage.Should().BeNull();
        }
        fixture.Bodies.Should().ContainSingle();
    }

    [Fact]
    public async Task Unknown_length_stream_is_bounded_by_remaining_output_plus_one_and_disposed_without_success()
    {
        const int remaining = 512;
        await using var fixture = new Fixture();
        using var root = fixture.Root();
        var stream = new ControlledStream(Encoding.UTF8.GetBytes(new string(' ', remaining + 20)));
        stream.Release.TrySetResult();
        fixture.Chat = (message, _) => Task.FromResult(Streaming(message, stream));
        var reply = await fixture.Transport.CompleteAsync(
            fixture.Request(budget: new(ModelToolProtocol.MaximumInputUtf8Bytes, remaining)), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Denied);
        reply.Message.Should().BeNull();
        reply.Usage.Should().BeNull();
        stream.ReadBytes.Should().Be(remaining + 1);
        stream.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, ModelTurnOutcome.Succeeded)]
    [InlineData(-1, ModelTurnOutcome.Denied)]
    public async Task Canonical_output_charge_cannot_be_understated_by_shorter_native_tool_calls(
        int adjustment, ModelTurnOutcome expected)
    {
        await using var fixture = new Fixture { BoundModel = "m" };
        using var root = fixture.Root();
        var text = JsonSerializer.Serialize(new
        {
            model = fixture.BoundModel,
            message = new
            {
                role = "assistant",
                content = "",
                tool_calls = Enumerable.Range(0, ModelToolProtocol.MaximumCalls)
                    .Select(_ => new { function = new { name = ReadOnlyCapabilityCatalog.Version, arguments = new Dictionary<string, string>(StringComparer.Ordinal) } }),
            },
            done = true,
        });
        var bytes = Encoding.UTF8.GetBytes(text);
        var canonical = OllamaToolWire.ReadReply(bytes, fixture.BoundModel, 1).Message;
        var canonicalBytes = ModelToolProtocol.SerializeMessage(canonical).Length;
        canonicalBytes.Should().BeGreaterThan(bytes.Length);
        fixture.Chat = (message, _) => Task.FromResult(Json(message, text));
        var reply = await fixture.Transport.CompleteAsync(
            fixture.Request(budget: new(ModelToolProtocol.MaximumInputUtf8Bytes, canonicalBytes + adjustment)), Token);
        reply.Outcome.Should().Be(expected);
        if (expected == ModelTurnOutcome.Succeeded)
        {
            reply.Message.Should().BeOfType<ModelAssistantToolCalls>().Which.Calls
                .Should().HaveCount(ModelToolProtocol.MaximumCalls);
            reply.Usage!.OutputUtf8Bytes.Should().Be(canonicalBytes);
        }
        else
        {
            reply.Message.Should().BeNull();
            reply.Usage.Should().BeNull();
        }
        fixture.Bodies.Should().ContainSingle();
    }

    private static string NativeAnswer(string model) => JsonSerializer.Serialize(new
    {
        model,
        message = new { role = "assistant", content = "Genuine answer" },
        done = true,
        done_reason = "stop",
    });
}
