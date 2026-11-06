using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteEvidenceSinkTests
{
    [Fact]
    public void Signature_encoding_roundtrips_and_legacy_absent_field_remains_compatible()
    {
        var signature = ReportSignature();
        var envelope = Bootstrap() with { ExceptionType = signature.ExceptionType, ExceptionReport = signature };
        var payload = WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(envelope);
        WindowsSqliteEvidenceSink.DecodeDiagnostic(payload).ExceptionReport.Should().Be(signature);
        var legacy = JsonNode.Parse(payload)!.AsObject();
        legacy.Remove("ExceptionReport");
        var restored = WindowsSqliteEvidenceSink.DecodeDiagnostic(legacy.ToJsonString());
        restored.ExceptionType.Should().Be(signature.ExceptionType);
        restored.ExceptionReport.Should().BeNull();
        restored.SchemaVersion.Should().Be(1);
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("stack")]
    [InlineData("format")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("type")]
    public void Invalid_present_signatures_are_rejected_instead_of_silently_removed(string corruption)
    {
        var signature = ReportSignature();
        var envelope = Bootstrap() with { ExceptionType = signature.ExceptionType, ExceptionReport = signature };
        var payload = JsonNode.Parse(WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(envelope))!;
        var data = payload["ExceptionReport"]!;
        switch (corruption)
        {
            case "fingerprint": data["Fingerprint"] = new string('0', 64); break;
            case "stack": data["StackTrace"] = "C:\\private\\source.cs:line 123"; break;
            case "format": data["FormatVersion"] = 2; break;
            case "missing": data.AsObject().Remove("StackTrace"); break;
            case "unknown": data["Unknown"] = true; break;
            case "type": payload["ExceptionType"] = "System.Exception"; break;
        }
        var encoded = payload.ToJsonString();
        if (string.Equals(corruption, "duplicate", StringComparison.Ordinal))
        {
            var original = data.ToJsonString();
            encoded = encoded.Replace(original, original[..^1] + ",\"FormatVersion\":1}", StringComparison.Ordinal);
        }
        var read = () => WindowsSqliteEvidenceSink.DecodeDiagnostic(encoded);
        if (corruption is "missing" or "unknown") { read.Should().Throw<System.Text.Json.JsonException>(); }
        else { read.Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void Admission_rejects_bad_signature_before_writing()
    {
        var signature = ReportSignature();
        var invalid = Bootstrap() with
        {
            ExceptionType = signature.ExceptionType,
            ExceptionReport = signature with { Fingerprint = "invalid" },
        };
        var encode = () => WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(invalid);
        encode.Should().Throw<InvalidDataException>();
        var mismatched = invalid with { ExceptionReport = signature, ExceptionType = "System.Exception" };
        var encodeMismatch = () => WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(mismatched);
        encodeMismatch.Should().Throw<InvalidDataException>();
    }

    private static ExceptionReportSignature ReportSignature()
    {
        return ExceptionReportSignature.Create("System.IO.IOException", "Fixture.Worker::Run(System.Int32)\nFixture.Host::Start()");
    }
}
