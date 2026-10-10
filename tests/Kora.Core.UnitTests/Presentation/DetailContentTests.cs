using System.Diagnostics;
using System.Text;
using AwesomeAssertions;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Core.UnitTests.Presentation;

[Collection("Host tracing")]
public sealed class DetailContentTests
{
    [Fact]
    public void History_ownership_is_required_distinct_and_never_fabricates_response_ids()
    {
        var reference = Reference();
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        var item = new AdmittedDetailContent(reference, DetailContentKind.PlainText, DetailContentOrigin.SessionHistory,
            DetailSensitivity.DisclosureConfirmationRequired, "Receipt", "Persisted history", "record", historySession: session);
        item.HistorySession.Should().Be(session);
        item.SessionSource.Should().BeNull();
        item.IsSameSnapshot(new(reference, item.Kind, item.Origin, item.Sensitivity, item.Title, item.Provenance,
            item.Source, historySession: session)).Should().BeTrue();
        item.IsSameSnapshot(new(reference, item.Kind, item.Origin, item.Sensitivity, item.Title, item.Provenance,
            item.Source, historySession: new(Guid.NewGuid()))).Should().BeFalse();
        var missing = () => new AdmittedDetailContent(reference, item.Kind, item.Origin, item.Sensitivity,
            item.Title, item.Provenance, item.Source);
        missing.Should().Throw<InvalidDataException>();
        var fabricated = () => new AdmittedDetailContent(reference, item.Kind, DetailContentOrigin.EmbeddedDocument,
            item.Sensitivity, item.Title, item.Provenance, item.Source, historySession: session);
        fabricated.Should().Throw<InvalidDataException>();
        var invalid = () => new AdmittedDetailContent(reference, item.Kind, item.Origin, item.Sensitivity,
            item.Title, item.Provenance, item.Source, historySession: default(HostId<SessionIdentity>));
        invalid.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Content_preserves_exact_immutable_source_and_host_chrome()
    {
        var reference = Reference();
        var item = Create(reference, source: "α\r\n\ttext\n");
        item.Reference.Should().Be(reference);
        item.Kind.Should().Be(DetailContentKind.Markdown);
        item.Origin.Should().Be(DetailContentOrigin.EmbeddedDocument);
        item.Sensitivity.Should().Be(DetailSensitivity.Public);
        item.Title.Should().Be("Host title");
        item.Provenance.Should().Be("Embedded guide");
        item.Source.Should().Be("α\r\n\ttext\n");
        item.Utf8Bytes.Should().Be(Encoding.UTF8.GetByteCount(item.Source));
        item.Digest.Should().HaveLength(64);
        item.SessionSource.Should().BeNull();
        item.IsSameSnapshot(Create(reference, source: item.Source)).Should().BeTrue();
    }

    [Fact]
    public void Finalized_response_requires_exact_host_ids_and_keeps_content_untrusted()
    {
        var host = new DetailSessionSource(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()));
        var item = Create(Reference(), origin: DetailContentOrigin.FinalizedResponse, session: host,
            sensitivity: DetailSensitivity.Private, kind: DetailContentKind.PlainText);
        item.SessionSource.Should().BeSameAs(host);
        item.SessionSource.SessionId.Should().Be(host.SessionId);
        item.SessionSource.RequestId.Should().Be(host.RequestId);
        item.SessionSource.TaskId.Should().Be(host.TaskId);
        item.Origin.Should().Be(DetailContentOrigin.FinalizedResponse);
        item.Kind.Should().Be(DetailContentKind.PlainText);
        item.Sensitivity.Should().Be(DetailSensitivity.Private);
    }

    [Fact]
    public void Missing_default_identity_and_nonpositive_revisions_are_rejected()
    {
        var identity = () => new DetailContentReference(default, 1);
        identity.Should().Throw<InvalidDataException>();
        var zero = () => new DetailContentReference(new(Guid.NewGuid()), 0);
        zero.Should().Throw<ArgumentOutOfRangeException>();
        var negative = () => new DetailContentReference(new(Guid.NewGuid()), -1);
        negative.Should().Throw<ArgumentOutOfRangeException>();
        var missing = () => Create(default);
        missing.Should().Throw<InvalidDataException>();
        Reference().Validate();
        // A value type's default is never an admitted reference.
        var validate = () => default(DetailContentReference).Validate();
        validate.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Malformed_host_classifications_are_not_reinterpreted(int classification)
    {
        var create = () => Create(Reference(),
            kind: classification == 1 ? (DetailContentKind)99 : DetailContentKind.Markdown,
            origin: classification == 2 ? (DetailContentOrigin)99 : DetailContentOrigin.EmbeddedDocument,
            sensitivity: classification == 3 ? (DetailSensitivity)99 : DetailSensitivity.Public);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Origin_cannot_fabricate_or_omit_session_authority(bool embedded)
    {
        var create = () => Create(Reference(),
            origin: embedded ? DetailContentOrigin.EmbeddedDocument : DetailContentOrigin.FinalizedResponse,
            session: embedded ? new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid())) : null);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Default_session_request_or_task_identity_is_rejected(int missing)
    {
        var source = new DetailSessionSource(
            missing == 1 ? default : new(Guid.NewGuid()),
            missing == 2 ? default : new(Guid.NewGuid()),
            missing == 3 ? default : new(Guid.NewGuid()));
        var create = () => Create(Reference(), origin: DetailContentOrigin.FinalizedResponse, session: source);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("", "guide", "source")]
    [InlineData("title", "", "source")]
    [InlineData("title", "guide", "")]
    public void Empty_content_and_empty_native_labels_fail_explicitly(string title, string provenance, string source)
    {
        var create = () => Create(Reference(), title: title, provenance: provenance, source: source);
        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Chrome_labels_are_bounded_independently_of_content(bool title)
    {
        Create(Reference(), title: new('t', 256), provenance: new('p', 256));
        var create = () => Create(Reference(), title: title ? new('t', 257) : "Title",
            provenance: title ? "Guide" : new('p', 257));
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Byte_boundary_counts_utf8_not_characters_and_never_truncates()
    {
        var exact = new string('é', NativeDetailProfile.MaximumUtf8Bytes / 2);
        Create(Reference(), source: exact).Source.Should().Be(exact);
        var tooLarge = () => Create(Reference(), source: exact + "a");
        tooLarge.Should().Throw<InvalidDataException>().WithMessage("*no text was truncated*");
        var invalidUnicode = () => Create(Reference(), source: "\ud800");
        invalidUnicode.Should().Throw<EncoderFallbackException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Same_reference_must_not_replace_any_source_or_native_metadata(int difference)
    {
        var reference = Reference();
        var original = Create(reference);
        var session = difference == 8
            ? new DetailSessionSource(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid())) : null;
        var changed = Create(
            difference == 1 ? Reference() : reference,
            kind: difference == 2 ? DetailContentKind.PlainText : DetailContentKind.Markdown,
            origin: difference is 3 or 8 ? DetailContentOrigin.FinalizedResponse : DetailContentOrigin.EmbeddedDocument,
            session: difference is 3 or 8 ? session ?? new(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid())) : null,
            sensitivity: difference == 4 ? DetailSensitivity.Private : DetailSensitivity.Public,
            title: difference == 5 ? "Different" : "Host title",
            provenance: difference == 6 ? "Different" : "Embedded guide",
            source: difference == 7 ? "Different" : "source");
        original.IsSameSnapshot(changed).Should().BeFalse();
        var nullOther = () => original.IsSameSnapshot(null!);
        nullOther.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Distinct_host_sources_on_the_same_finalized_reference_conflict()
    {
        var reference = Reference();
        var first = new DetailSessionSource(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()));
        var second = first with { RequestId = new(Guid.NewGuid()) };
        Create(reference, origin: DetailContentOrigin.FinalizedResponse, session: first)
            .IsSameSnapshot(Create(reference, origin: DetailContentOrigin.FinalizedResponse, session: second))
            .Should().BeFalse();
    }

    [Fact]
    public void Admission_trace_reports_truthful_status_without_content_or_paths()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Core", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        Create(Reference(), source: "secret source", title: "secret title", provenance: @"Q:\secret\file");
        var reject = () => Create(Reference(), kind: (DetailContentKind)99);
        reject.Should().Throw<InvalidDataException>();
        stopped.Select(item => item.Status).Should().Equal(ActivityStatusCode.Ok, ActivityStatusCode.Error);
        stopped.Should().OnlyContain(item => item.OperationName == "presentation.admit");
        string.Join(';', stopped.SelectMany(item => item.TagObjects).Select(tag => tag.Value?.ToString()))
            .Should().NotContain("secret");
        stopped[0].GetTagItem("kora.presentation.profile").Should().Be(NativeDetailProfile.Name);
    }

    private static DetailContentReference Reference() => new(new(Guid.NewGuid()), 1);

    private static AdmittedDetailContent Create(DetailContentReference reference,
        DetailContentKind kind = DetailContentKind.Markdown,
        DetailContentOrigin origin = DetailContentOrigin.EmbeddedDocument,
        DetailSensitivity sensitivity = DetailSensitivity.Public,
        string title = "Host title", string provenance = "Embedded guide", string source = "source",
        DetailSessionSource? session = null) =>
        new(reference, kind, origin, sensitivity, title, provenance, source, session);
}
