using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

public sealed class WebResultSnapshotTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("Exact 😀 é\n<script>not executable</script> https://example.com/image", true)]
    public void CapturePreservesExactReturnedTextAndCompleteUntrustedProvenance(string text, bool truncated)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var reference = new DetailContentReference(new(Guid.NewGuid()), 1);
        var requested = new Uri("https://example.com/" + new string('a', 1024));
        var result = new WebPageGetResult(WebPageGetOutcome.Succeeded, "retrieved",
            new("https://example.com/final"), "text/html", text, truncated, 2);
        var time = DateTimeOffset.Parse("2026-10-10T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var content = WebResultSnapshot.Capture(reference, request, 3, requested, result, time);
        var snapshot = content.WebResult!;
        snapshot.Reference.Should().Be(reference);
        snapshot.Text.Should().Be(text);
        snapshot.Provenance.OriginalRequest.Should().BeSameAs(request);
        snapshot.Provenance.ControlGeneration.Should().Be(3);
        snapshot.Provenance.RequestedAddress.Should().Be(requested.AbsoluteUri);
        snapshot.Provenance.FinalAddress.Should().Be(result.FinalAddress!.AbsoluteUri);
        snapshot.Provenance.MediaType.Should().Be("text/html");
        snapshot.Provenance.RedirectCount.Should().Be(2);
        snapshot.Provenance.Truncated.Should().Be(truncated);
        snapshot.Provenance.RetrievedAt.Should().Be(time);
        snapshot.Provenance.ReturnedTextSha256.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
        snapshot.Provenance.ReturnedTextUtf8Bytes.Should().Be(Encoding.UTF8.GetByteCount(text));
        snapshot.Provenance.MaximumRawContentUtf8Bytes.Should().Be(256 * 1024);
        snapshot.Provenance.MaximumNormalizedTextUtf8Bytes.Should().Be(60 * 1024);
        snapshot.Provenance.MaximumSerializedToolResultUtf8Bytes.Should().Be(64 * 1024);
        snapshot.Provenance.MaximumNativeSourceUtf8Bytes.Should().Be(256 * 1024);
        content.Source.Should().Be(snapshot.Source).And.EndWith(text);
        content.Source.Should().Contain(requested.AbsoluteUri).And.Contain("VOLATILE").And.Contain("untrusted");
        content.Source.Should().Contain(snapshot.Provenance.ReturnedTextSha256);
        content.Digest.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.Source))));
        content.Origin.Should().Be(DetailContentOrigin.RetrievedWebResult);
        content.Kind.Should().Be(DetailContentKind.PlainText);
        content.Sensitivity.Should().Be(DetailSensitivity.DisclosureConfirmationRequired);
        content.SessionSource.Should().BeNull();
        content.HistorySession.Should().BeNull();
        content.Title.Length.Should().BeLessThanOrEqualTo(256);
        content.Provenance.Length.Should().BeLessThanOrEqualTo(256);
        content.IsSameSnapshot(new(reference, content.Kind, content.Origin, content.Sensitivity,
            content.Title, content.Provenance, content.Source, webResult: snapshot)).Should().BeTrue();
        content.IsSameSnapshot(WebResultSnapshot.Capture(reference, request, 3, requested, result, time)).Should().BeFalse();
        var deserialize = () => JsonSerializer.Deserialize<WebResultSnapshot>(JsonSerializer.Serialize(snapshot));
        deserialize.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void CaptureRejectsMissingOrInvalidSuccessState(int invalid)
    {
        var result = new WebPageGetResult(
            invalid == 0 ? WebPageGetOutcome.Failed : WebPageGetOutcome.Succeeded, "retrieved",
            invalid == 2 ? null : new("https://example.com/"),
            invalid == 3 ? "application/json" : "text/plain", invalid == 1 ? null : "",
            RedirectCount: invalid == 4 ? -1 : invalid == 5 ? 6 : 0);
        var capture = () => WebResultSnapshot.Capture(new(new(Guid.NewGuid()), 1),
            HostRequest.Create(RequestOrigin.LocalUi), invalid == 6 ? 0 : 1,
            new("https://example.com/"), result, invalid == 7 ? default : DateTimeOffset.UtcNow);
        capture.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void CaptureRejectsMissingHostResultAndIllFormedUnicodeWithoutReplacementOrTruncation()
    {
        var result = new WebPageGetResult(WebPageGetOutcome.Succeeded, "retrieved",
            new("https://example.com/"), "text/plain", "");
        var host = HostRequest.Create(RequestOrigin.LocalUi);
        var reference = new DetailContentReference(new(Guid.NewGuid()), 1);
        var missingHost = () => WebResultSnapshot.Capture(reference, null!, 1, result.FinalAddress!, result, DateTimeOffset.UtcNow);
        missingHost.Should().Throw<ArgumentNullException>();
        var missingResult = () => WebResultSnapshot.Capture(reference, host, 1, result.FinalAddress!, null!, DateTimeOffset.UtcNow);
        missingResult.Should().Throw<ArgumentNullException>();
        var invalidUnicode = () => WebResultSnapshot.Capture(reference, host, 1, result.FinalAddress!,
            result with { Text = "\ud800" }, DateTimeOffset.UtcNow);
        invalidUnicode.Should().Throw<EncoderFallbackException>();
        var oversize = () => WebResultSnapshot.Capture(reference, host, 1, result.FinalAddress!,
            result with { Text = new string('a', WebPageCapability.MaximumTextUtf8Bytes + 1) }, DateTimeOffset.UtcNow);
        oversize.Should().Throw<InvalidDataException>();
        WebResultSnapshot.Capture(reference, host, 1, result.FinalAddress!,
            result with { Text = new string('a', WebPageCapability.MaximumTextUtf8Bytes) }, DateTimeOffset.UtcNow)
            .WebResult!.Text.Length.Should().Be(WebPageCapability.MaximumTextUtf8Bytes);
        var metadataOversize = () => WebResultSnapshot.Capture(reference, host, 1, result.FinalAddress!,
            result with { FinalAddress = new("https://example.com/" + new string('a', NativeDetailProfile.MaximumUtf8Bytes)) },
            DateTimeOffset.UtcNow);
        metadataOversize.Should().Throw<InvalidDataException>().WithMessage("*no text was truncated*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AdmissionRejectsReclassificationForeignReferenceAndChangedSource(int invalid)
    {
        var content = WebResultSnapshot.Capture(new(new(Guid.NewGuid()), 1), HostRequest.Create(RequestOrigin.LocalUi),
            1, new("https://example.com/"),
            new(WebPageGetOutcome.Succeeded, "retrieved", new("https://example.com/"), "text/plain", ""), DateTimeOffset.UtcNow);
        var admit = () => new AdmittedDetailContent(
            invalid == 4 ? new(new(Guid.NewGuid()), 1) : content.Reference,
            invalid == 2 ? DetailContentKind.Markdown : content.Kind,
            invalid == 1 ? DetailContentOrigin.EmbeddedDocument : content.Origin,
            invalid == 3 ? DetailSensitivity.Public : content.Sensitivity,
            content.Title, content.Provenance, invalid == 5 ? "changed" : content.Source,
            webResult: invalid == 0 ? null : content.WebResult);
        admit.Should().Throw<InvalidDataException>();
    }
}
