using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class WindowsOllamaToolTransportTests
{
    [Fact]
    public async Task NativeLocalTransportAdvertisesOnlyReviewedMetadataListAndOpaqueGetSchema()
    {
        await using var f = new Fixture();
        using var root = f.Root();
        f.Chat = (message, _) => Task.FromResult(Json(message, Final));
        var reply = await f.Transport.CompleteAsync(f.Request(toolMode: ModelToolMode.CustomizationDiscovery), Token);
        reply.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
        using var sent = JsonDocument.Parse(f.Bodies.Single());
        var tools = sent.RootElement.GetProperty("tools").EnumerateArray().ToArray();
        tools.Select(tool => tool.GetProperty("function").GetProperty("name").GetString())
            .Should().Equal(CustomizationDiscoveryTools.List, CustomizationDiscoveryTools.Get);
        var get = tools[1].GetProperty("function").GetProperty("parameters");
        get.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        get.GetProperty("required")[0].GetString().Should().Be("ref");
        get.GetProperty("properties").EnumerateObject().Should().ContainSingle().Which.Name.Should().Be("ref");
        get.GetProperty("properties").GetProperty("ref").GetProperty("minLength").GetInt32().Should().Be(32);
        get.GetProperty("properties").GetProperty("ref").GetProperty("maxLength").GetInt32().Should().Be(32);
    }
}
