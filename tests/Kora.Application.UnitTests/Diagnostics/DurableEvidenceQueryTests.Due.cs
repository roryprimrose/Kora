using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed partial class DurableEvidenceQueryTests
{
    [Fact]
    public async Task DueBoundsBindTheCompleteVersionTwoSemanticDigestAndAuthenticatedCursor()
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(2);
        var edge = DateTimeOffset.UnixEpoch;
        var query = new EvidenceQuery { Limit = 1, FromDueUtc = edge, UntilDueUtc = edge.AddDays(1) };
        var cursor = (await fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken)).Cursor!;
        var format = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var expected = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { Version = 2, Query = query }, format)));
        using var payload = JsonDocument.Parse(Convert.FromBase64String(cursor.Split('.')[0]));
        payload.RootElement.GetProperty("Fingerprint").GetString().Should().Be(expected);
        EvidenceQuery[] changed =
        [
            query with { FromDueUtc = null }, query with { FromDueUtc = edge.AddTicks(1) },
            query with { UntilDueUtc = null }, query with { UntilDueUtc = edge.AddDays(1).AddTicks(-1) },
            query with { FromUtc = edge }, query with { UntilUtc = edge },
        ];
        foreach (var value in changed)
        {
            var read = () => fixture.Service.QueryAsync(value, cursor, TestContext.Current.CancellationToken).AsTask();
            await read.Should().ThrowAsync<InvalidDataException>();
        }
        fixture.Reader.Calls.Should().Be(1);
        await fixture.Service.QueryAsync(query, cursor, TestContext.Current.CancellationToken);
        fixture.Reader.Calls.Should().Be(2);
        var serialized = JsonSerializer.Serialize(query, format);
        var restored = JsonSerializer.Deserialize<EvidenceQuery>(serialized, format)!;
        restored.Should().Be(query);
        var defaults = JsonSerializer.Deserialize<EvidenceQuery>("{}", format)!;
        defaults.Should().Be(new EvidenceQuery());
        var page = await fixture.Service.QueryAsync(defaults, null, TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(DurableEvidenceQuery.Serialize(page));
        json.RootElement.GetProperty("Records")[0].GetProperty("DueUtc").GetDateTimeOffset()
            .Should().Be(fixture.Reader.Batch.Candidates[0].Record.DueUtc);
    }

    [Fact]
    public async Task InvalidDueOrderAndNonzeroOffsetsFailBeforeReaderWithoutNormalization()
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        var edge = DateTimeOffset.UnixEpoch;
        EvidenceQuery[] invalid =
        [
            new() { FromDueUtc = edge.AddTicks(1), UntilDueUtc = edge },
            new() { FromDueUtc = edge.ToOffset(TimeSpan.FromHours(1)) },
            new() { UntilDueUtc = edge.ToOffset(TimeSpan.FromHours(-1)) },
        ];
        foreach (var query in invalid)
        {
            var read = () => fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken).AsTask();
            await read.Should().ThrowAsync<ArgumentException>();
        }
        fixture.Reader.Calls.Should().Be(0);
        foreach (var source in Enum.GetValues<EvidenceSource>())
        foreach (var bounds in new EvidenceQuery[]
        {
            new(), new() { FromDueUtc = DateTimeOffset.MinValue }, new() { UntilDueUtc = DateTimeOffset.MaxValue },
            new() { FromDueUtc = edge, UntilDueUtc = edge },
            new() { FromDueUtc = DateTimeOffset.MinValue, UntilDueUtc = DateTimeOffset.MaxValue },
        })
        {
            DurableEvidenceQuery.Validate(bounds with { Source = source, FromUtc = edge, UntilUtc = edge });
        }
    }
}
