using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed partial class DurableEvidenceQueryTests
{
    [Fact]
    public async Task Maximum_segment_inventory_round_trips_all_ceilings_within_the_serialized_page_budget()
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        var segments = Enumerable.Range(0, 128).Select(index => new EvidenceSegmentSnapshot(
            Guid.NewGuid().ToString("N"), index * 100000L,
            new(long.MaxValue / 2, long.MaxValue / 2, long.MaxValue / 2, long.MaxValue / 2,
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D") + ":31"))).ToArray();
        var snapshot = new EvidenceSnapshot(0, 0, 0, 0,
            Segmented: new(SegmentedEvidenceSnapshot.CurrentVersion, Guid.NewGuid().ToString("N"), 127, segments));
        fixture.Reader.Batch = Batch(2) with { Snapshot = snapshot };
        var query = new EvidenceQuery { Limit = 1 };
        var first = await fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        first.Records.Should().ContainSingle();
        first.Cursor.Should().NotBeNull();
        DurableEvidenceQuery.Serialize(first).Length.Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
        var frame = Convert.FromBase64String(first.Cursor!.Split('.')[0]);
        var header = "KoraCursorV2:"u8.ToArray();
        frame.Take(header.Length).Should().Equal(header);
        var inflated = new byte[DurableEvidenceQuery.MaximumInflatedCursorBytes];
        BrotliDecoder.TryDecompress(frame.AsSpan(header.Length), inflated, out var written).Should().BeTrue();
        var uncompressedCursorCharacters = ((written + 2) / 3 * 4) + 1 + 44;
        uncompressedCursorCharacters.Should().BeGreaterThan(EvidencePage.MaximumBytes);
        await fixture.Service.QueryAsync(query, first.Cursor, TestContext.Current.CancellationToken);
        fixture.Reader.Checkpoint!.Snapshot.Should().BeEquivalentTo(snapshot);
        fixture.Reader.Checkpoint.After.Should().Be(fixture.Reader.Batch.Candidates[0].Position);
    }

    [Fact]
    public async Task Segment_snapshot_cursor_retains_session_filter_and_authentication_boundaries()
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(2) with
        {
            Snapshot = new(0, 0, 0, 0, Segmented: new(SegmentedEvidenceSnapshot.CurrentVersion, "store", 1,
                [new("legacy", 0, new(4, 5, 6, 7))])),
        };
        var query = new EvidenceQuery { Limit = 1 };
        var first = await fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        var changedQuery = () => fixture.Service.QueryAsync(query with { Limit = 2 }, first.Cursor,
            TestContext.Current.CancellationToken).AsTask();
        await changedQuery.Should().ThrowAsync<InvalidDataException>();
        var cursor = first.Cursor!;
        var signatureStart = cursor.IndexOf('.', StringComparison.Ordinal) + 1;
        var changedSignature = cursor[..signatureStart] + (cursor[signatureStart] == 'A' ? "B" : "A")
            + cursor[(signatureStart + 1)..];
        var tampered = () => fixture.Service.QueryAsync(query, changedSignature, TestContext.Current.CancellationToken).AsTask();
        await tampered.Should().ThrowAsync<InvalidDataException>();
        using var foreign = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var crossSession = () => fixture.Service.QueryAsync(query, first.Cursor, TestContext.Current.CancellationToken).AsTask();
        await crossSession.Should().ThrowAsync<InvalidDataException>();
        fixture.Reader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Legacy_signed_cursors_remain_readable_and_compressed_wire_bound_is_exact()
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(2);
        var query = new EvidenceQuery { Limit = 1 };
        var first = await fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        var legacy = first.Cursor!;
        var legacyBytes = Convert.FromBase64String(legacy.Split('.')[0]);
        legacyBytes[0].Should().Be((byte)'{');
        await fixture.Service.QueryAsync(query, legacy, TestContext.Current.CancellationToken);
        fixture.Reader.Checkpoint!.Snapshot.Should().BeEquivalentTo(fixture.Reader.Batch.Snapshot);
        var oversizedLegacy = legacy.Insert(legacy.IndexOf('.', StringComparison.Ordinal),
            new string(' ', 2049 - legacy.Length));
        var rejectLegacy = () => fixture.Service.QueryAsync(query, oversizedLegacy,
            TestContext.Current.CancellationToken).AsTask();
        await rejectLegacy.Should().ThrowAsync<InvalidDataException>().WithMessage("*legacy*bound*");
        var compressed = Sign(fixture, CompressCursor(legacyBytes));
        var dot = compressed.IndexOf('.', StringComparison.Ordinal);
        var atLimit = compressed.Insert(dot, new string(' ', DurableEvidenceQuery.MaximumCursorCharacters - compressed.Length));
        atLimit.Length.Should().Be(48 * 1024);
        await fixture.Service.QueryAsync(query, atLimit, TestContext.Current.CancellationToken);
        var calls = fixture.Reader.Calls;
        var oversized = () => fixture.Service.QueryAsync(query, atLimit + " ",
            TestContext.Current.CancellationToken).AsTask();
        await oversized.Should().ThrowAsync<InvalidDataException>();
        fixture.Reader.Calls.Should().Be(calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Compressed_cursor_enforces_the_exact_inflated_bound_before_reading(int excess)
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(2);
        var query = new EvidenceQuery { Limit = 1 };
        var first = await fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        var json = Convert.FromBase64String(first.Cursor!.Split('.')[0]);
        var expanded = new byte[DurableEvidenceQuery.MaximumInflatedCursorBytes + excess];
        expanded.AsSpan().Fill((byte)' ');
        json.CopyTo(expanded, 0);
        var cursor = Sign(fixture, CompressCursor(expanded));
        cursor.Length.Should().BeLessThan(DurableEvidenceQuery.MaximumCursorCharacters);
        var resume = () => fixture.Service.QueryAsync(query, cursor, TestContext.Current.CancellationToken).AsTask();
        if (excess == 0)
        {
            await resume();
            fixture.Reader.Calls.Should().Be(2);
        }
        else
        {
            await resume.Should().ThrowAsync<InvalidDataException>();
            fixture.Reader.Calls.Should().Be(1);
        }
    }

    [Theory]
    [InlineData("truncated")]
    [InlineData("trailing")]
    [InlineData("corrupt")]
    [InlineData("version")]
    [InlineData("json")]
    public async Task Authenticated_but_invalid_compressed_payloads_fail_before_the_reader(string corruption)
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        fixture.Reader.Batch = Batch(2);
        var query = new EvidenceQuery { Limit = 1 };
        var first = await fixture.Service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        var json = Convert.FromBase64String(first.Cursor!.Split('.')[0]);
        var bytes = CompressCursor(json);
        bytes = corruption switch
        {
            "truncated" => bytes[..^1],
            "trailing" => [.. bytes, 0],
            "corrupt" => [.. Encoding.UTF8.GetBytes("KoraCursorV2:"), 255, 255, 255, 255],
            "version" => [.. Encoding.UTF8.GetBytes("KoraCursorV3:"), .. bytes[13..]],
            "json" => CompressCursor("{"u8.ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
        var resume = () => fixture.Service.QueryAsync(query, Sign(fixture, bytes),
            TestContext.Current.CancellationToken).AsTask();
        await resume.Should().ThrowAsync<InvalidDataException>();
        fixture.Reader.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cursor_encoding_never_emits_an_oversized_payload_or_wire_token(bool incompressible)
    {
        using var listener = Listen();
        using var host = Root();
        var fixture = new Fixture();
        var identity = incompressible
            ? Convert.ToHexString(RandomNumberGenerator.GetBytes(48 * 1024))
            : new string('x', DurableEvidenceQuery.MaximumInflatedCursorBytes);
        fixture.Reader.Batch = Batch(2) with
        {
            Snapshot = new(0, 0, 0, 0, Segmented: new(SegmentedEvidenceSnapshot.CurrentVersion, identity, 1,
                [new("legacy", 0, new(0, 0, 0, 0))])),
        };
        var read = () => fixture.Service.QueryAsync(new() { Limit = 1 }, null, TestContext.Current.CancellationToken).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    private static string Sign(Fixture fixture, byte[] bytes) => Convert.ToBase64String(bytes) + "."
        + Convert.ToBase64String(HMACSHA256.HashData(fixture.Key, bytes));

    private static byte[] CompressCursor(byte[] bytes)
    {
        using var stream = new MemoryStream();
        stream.Write("KoraCursorV2:"u8);
        using (var compressor = new BrotliStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        {
            compressor.Write(bytes);
        }
        return stream.ToArray();
    }
}
