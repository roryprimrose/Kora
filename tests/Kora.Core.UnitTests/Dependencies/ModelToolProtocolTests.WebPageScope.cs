using System.Collections.Immutable;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Network;
using Kora.Core.Tools;

namespace Kora.Core.UnitTests.Dependencies;

public sealed partial class ModelToolProtocolTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("""{"Destinations":[]}""")]
    [InlineData("""{"Destinations":["https://one.example/"],"MaximumCalls":3}""")]
    [InlineData("""{"Destinations":["https://one.example/"],"Destinations":["https://two.example/"]}""")]
    [InlineData("""{"Destinations":["https://one.example/","https://one.example/"]}""")]
    [InlineData("""{"Destinations":["file:///C:/private"]}""")]
    public void WebScopeRejectsMalformedExpandedOrDuplicateWire(string scope)
    {
        var provenance = Provenance();
        var envelope = new ModelContextEnvelope(provenance.Request, "policy", "request", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.LocalWebPage);
        var wire = Encoding.UTF8.GetString(envelope.Serialize()).Replace("\"MaximumOutputUtf8Bytes\":",
            "\"WebPages\":" + scope + ",\"MaximumOutputUtf8Bytes\":", StringComparison.Ordinal);
        FluentActions.Invoking(() => ModelToolProtocol.ReadContext(Encoding.UTF8.GetBytes(wire), provenance))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("default")]
    [InlineData("empty")]
    [InlineData("three")]
    [InlineData("duplicate")]
    [InlineData("equivalent")]
    [InlineData("relative")]
    [InlineData("credentials")]
    [InlineData("fragment")]
    [InlineData("scheme")]
    [InlineData("null")]
    public void WebScopeRejectsInvalidOrEqualExplicitDestinations(string fault)
    {
        ImmutableArray<Uri> destinations = fault switch
        {
            "default" => default,
            "empty" => [],
            "three" => [new("https://one.example/"), new("https://two.example/"), new("https://three.example/")],
            "duplicate" => [new("https://one.example/"), new("https://one.example/")],
            "equivalent" => [new("https://ONE.example:443/"), new("https://one.example/")],
            "relative" => [new("relative", UriKind.Relative)],
            "credentials" => [new("https://user:password@one.example/")],
            "fragment" => [new("https://one.example/#fragment")],
            "scheme" => [new("file:///C:/private")],
            _ => [null!],
        };
        Action act = () => _ = new LocalWebPageScope(destinations);
        if (fault is "relative" or "credentials" or "fragment" or "scheme" or "null")
        { act.Should().Throw<ArgumentException>(); }
        else { act.Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrozenWebScopeRoundTripsWithExactOneOrTwoCallAndRoundLimits(bool two)
    {
        var provenance = Provenance();
        var scope = new LocalWebPageScope(two ? [new("https://one.example/"), new("https://two.example/")] : [new("https://one.example/")]);
        var envelope = new ModelContextEnvelope(provenance.Request, "host policy", "exact original request", [],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.LocalWebPage, webPages: scope);
        envelope.WebPages.Should().BeSameAs(scope);
        var context = ModelToolProtocol.ReadContext(envelope.Serialize(), provenance);
        context.WebPages!.Destinations.Should().Equal(scope.Destinations);
        context.WebPages.MaximumCalls.Should().Be(two ? 2 : 1);
        context.WebPages.MaximumRounds.Should().Be(two ? 3 : 2);
        context.Tools.Should().Equal(LocalWebPageTool.Descriptor);
        var history = new List<ModelToolMessage>();
        for (var i = 0; i < scope.MaximumCalls; i++)
        {
            var assistant = Calls(Tool("page_" + i, WebPageCapability.Id, $$"""{"address":"{{scope.Destinations[i].AbsoluteUri}}"}"""));
            history.Add(assistant);
            history.Add(ModelToolProtocol.CreateToolResult(assistant.Calls[0], new(CapabilityOutcome.Succeeded, "inert",
                WebPage: new(provenance.Request, scope.Destinations[i], scope.Destinations[i], DateTimeOffset.UnixEpoch, "source", false, "text/plain"))));
        }
        ModelToolProtocol.CreateRequest(provenance, null, context, history, scope.MaximumRounds).Round.Should().Be(scope.MaximumRounds);
        Action beyond = () => ModelToolProtocol.CreateRequest(provenance, null, context, history, scope.MaximumRounds + 1);
        beyond.Should().Throw<InvalidDataException>();
        var forged = new ModelContextEnvelope(provenance.Request, "policy", "request", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.None);
        var wire = Encoding.UTF8.GetString(forged.Serialize()).Replace("\"MaximumOutputUtf8Bytes\":",
            "\"WebPages\":{\"Destinations\":[\"https://one.example/\"]},\"MaximumOutputUtf8Bytes\":", StringComparison.Ordinal);
        FluentActions.Invoking(() => ModelToolProtocol.ReadContext(Encoding.UTF8.GetBytes(wire), provenance)).Should().Throw<InvalidDataException>();
        FluentActions.Invoking(() => new ModelContextEnvelope(provenance.Request, "policy", "request", [], DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(15), ModelToolMode.ReadOnly, webPages: scope)).Should().Throw<InvalidDataException>();
    }
}
