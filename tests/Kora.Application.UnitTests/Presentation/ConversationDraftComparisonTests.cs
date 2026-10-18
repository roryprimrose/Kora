using AwesomeAssertions;

using Kora.Application.Presentation;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Presentation;

public sealed class ConversationDraftComparisonTests
{
    [Theory]
    [InlineData(null, "", "Absent observed saved body")]
    [InlineData("", "", "ordinally equal")]
    [InlineData("  e\u0301\U0001f642\r\n end  ", "  e\u0301\U0001f642\r\n end  ", "ordinally equal")]
    [InlineData("é", "e\u0301", "differ")]
    [InlineData("body\n", "body\r\n", "differ")]
    [InlineData("body", "body ", "differ")]
    public void FreezesOnlyExactBodiesAndDistinguishesAbsentBaseline(string? saved, string buffer, string conclusion)
    {
        var observed = Observed(saved);
        using var state = new ConversationDraftComparison(observed.Session, observed, buffer, () => true);
        state.Available.Should().BeTrue();
        state.SavedBody.Should().Be(saved);
        state.Buffer.Should().Be(buffer);
        state.Summary.Should().Contain(conclusion).And.Contain("Historical snapshots only")
            .And.Contain("no current disk freshness, semantic safety, Save, Submit or approval claim");
        state.BaselineLabel.Should().Contain(observed.Session.SessionId.Value.ToString("D"))
            .And.Contain("generation 3").And.Contain("observed saved revision 7").And.Contain("not current disk");
        observed.Draft.Body.Should().Be(saved);
    }

    [Fact]
    public void AdmitsCompleteMaximumTextWithoutRepairOrTruncation()
    {
        var maximum = new string('界', ConversationText.MaximumUtf16Characters - 6) + " \r\n\U0001f642 ";
        maximum.Length.Should().Be(ConversationText.MaximumUtf16Characters);
        var observed = Observed(maximum);
        using var state = new ConversationDraftComparison(observed.Session, observed, maximum, () => true);
        state.Buffer.Should().Be(maximum);
        state.SavedBody.Should().Be(maximum);
        state.Summary.Should().Contain("ordinally equal");
        using var inactive = new ConversationDraftComparison(observed.Session with { IsActive = false },
            observed with { Session = observed.Session with { IsActive = false } }, maximum, () => true);
        inactive.Available.Should().BeTrue("historical observation does not resume or require an Active session");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsEntireInvalidTextOnEitherSide(bool savedSide)
    {
        foreach (var invalid in new[]
        {
            new string('x', ConversationText.MaximumUtf16Characters + 1),
            new string('界', ConversationText.MaximumUtf8Bytes / 3 + 1),
            new string('\ud800', 1), "safe\u0000unsafe", "safe\ufffe",
        })
        {
            var observed = Observed(savedSide ? invalid : "valid");
            var create = () => new ConversationDraftComparison(observed.Session, observed, savedSide ? "valid" : invalid, () => true);
            create.Should().Throw<InvalidDataException>();
        }
        var credential = Observed(savedSide ? "password=abcdefghij123456" : "valid");
        var screen = () => new ConversationDraftComparison(credential.Session, credential,
            savedSide ? "valid" : "password=abcdefghij123456", () => true);
        screen.Should().Throw<ConversationCredentialException>();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("draft-session")]
    [InlineData("generation")]
    [InlineData("invalid-generation")]
    [InlineData("revision")]
    [InlineData("version")]
    public void RejectsUnknownOrMismatchedObservedIdentity(string invalid)
    {
        var observed = Observed("valid");
        var selected = observed.Session;
        switch (invalid)
        {
            case "session": selected = selected with { SessionId = new(Guid.NewGuid()) }; break;
            case "draft-session": observed = observed with { Draft = observed.Draft with { SessionId = new(Guid.NewGuid()) } }; break;
            case "generation": selected = selected with { Generation = new(4) }; break;
            case "invalid-generation":
                selected = selected with { Generation = default };
                observed = observed with { Session = selected };
                break;
            case "revision": observed = observed with { Draft = observed.Draft with { Revision = 0 } }; break;
            case "version": observed = observed with { Draft = observed.Draft with { Version = 2 } }; break;
        }
        var create = () => new ConversationDraftComparison(selected, observed, "valid", () => true);
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsNullAndRevokedAdmissionWithoutPublishingSnapshots()
    {
        var observed = Observed("private");
        var noTarget = () => new ConversationDraftComparison(null!, observed, "private", () => true);
        var noObserved = () => new ConversationDraftComparison(observed.Session, null!, "private", () => true);
        var noAdmission = () => new ConversationDraftComparison(observed.Session, observed, "private", null!);
        var noBuffer = () => new ConversationDraftComparison(observed.Session, observed, null!, () => true);
        var revoked = () => new ConversationDraftComparison(observed.Session, observed, "private", () => false);
        noTarget.Should().Throw<ArgumentNullException>();
        noObserved.Should().Throw<ArgumentNullException>();
        noAdmission.Should().Throw<ArgumentNullException>();
        noBuffer.Should().Throw<ArgumentNullException>();
        revoked.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("available")]
    [InlineData("saved")]
    [InlineData("buffer")]
    [InlineData("label")]
    [InlineData("summary")]
    [InlineData("dispose")]
    public void EveryAccessRetiresPrivateStringsAndCannotReviveAfterAdmissionReturns(string firstAccess)
    {
        var admitted = true;
        var observed = Observed("private baseline");
        using var state = new ConversationDraftComparison(observed.Session, observed, "private draft", () => admitted);
        admitted = false;
        switch (firstAccess)
        {
            case "available": state.Available.Should().BeFalse(); break;
            case "saved": state.SavedBody.Should().BeNull(); break;
            case "buffer": state.Buffer.Should().BeEmpty(); break;
            case "label": state.BaselineLabel.Should().BeEmpty(); break;
            case "summary": state.Summary.Should().BeEmpty(); break;
            case "dispose": state.Dispose(); break;
        }
        admitted = true;
        state.Available.Should().BeFalse();
        state.SavedBody.Should().BeNull();
        state.Buffer.Should().BeEmpty();
        state.BaselineLabel.Should().BeEmpty();
        state.Summary.Should().BeEmpty();
        state.Dispose();
    }

    private static ConversationDraftSnapshot Observed(string? body)
    {
        var session = new WorkSessionAuthorization(new HostId<SessionIdentity>(Guid.NewGuid()), new(3), true);
        return new(session, new(session.SessionId, 7, body));
    }
}
