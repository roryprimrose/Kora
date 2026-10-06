using System.Collections;
using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.Diagnostics;

public sealed class EvidencePropertiesTests
{
    [Fact]
    public void Every_supported_scalar_has_a_culture_independent_projection()
    {
        object?[] values = [null, true, false, (byte)1, (sbyte)-1, (short)-2, (ushort)2,
            3, 3U, 4L, 4UL, 1.25m, 1.25f, 1.25d, "safe", Guid.NewGuid(),
            DateTimeOffset.UtcNow, SecurityAuditOutcome.Unknown];
        foreach (var value in values)
        {
            var projected = EvidenceProperties.Capture(
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["Value"] = value });
            projected.Should().ContainKey("Value");
        }
        var nan = () => EvidenceProperties.Capture(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["Value"] = float.NaN });
        nan.Should().Throw<InvalidDataException>();
        var infinity = () => EvidenceProperties.Capture(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["Value"] = double.PositiveInfinity });
        infinity.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Missing_invalid_duplicate_and_over_bound_states_fail_explicitly()
    {
        var unstructured = () => EvidenceProperties.Capture("unstructured");
        unstructured.Should().Throw<InvalidDataException>();
        var duplicate = () => EvidenceProperties.Capture(new[]
        {
            new KeyValuePair<string, object?>("Count", 1),
            new KeyValuePair<string, object?>("Count", 2),
        });
        duplicate.Should().Throw<InvalidDataException>();
        var longName = () => EvidenceProperties.Capture(
            new Dictionary<string, object?>(StringComparer.Ordinal) { [new string('x', 129)] = 1 });
        longName.Should().Throw<InvalidDataException>();
        var tooMany = () => EvidenceProperties.Capture(Enumerable.Range(1, 33).Select(index =>
            new KeyValuePair<string, object?>(index.ToString(System.Globalization.CultureInfo.InvariantCulture), index)));
        tooMany.Should().Throw<InvalidDataException>();
        var missingTemplate = () => EvidenceProperties.Template(new Dictionary<string, object?>(StringComparer.Ordinal));
        missingTemplate.Should().Throw<InvalidDataException>();
        var unstructuredTemplate = () => EvidenceProperties.Template("text");
        unstructuredTemplate.Should().Throw<InvalidDataException>();
        var wrongTemplate = () => EvidenceProperties.Template(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["{OriginalFormat}"] = 1 });
        wrongTemplate.Should().Throw<InvalidDataException>();
        var oversizedTemplate = () => EvidenceProperties.Template(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["{OriginalFormat}"] = new string('x', 4097) });
        oversizedTemplate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Trusted_audit_state_has_a_non_generic_projection_without_granting_arbitrary_objects_authority()
    {
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "fixture", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "fixture");
        IEnumerable state = new TrustedAuditState(audit);
        var enumerator = state.GetEnumerator();
        try
        {
            enumerator.MoveNext().Should().BeTrue();
            enumerator.Current.Should().BeOfType<KeyValuePair<string, object?>>();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }
}
