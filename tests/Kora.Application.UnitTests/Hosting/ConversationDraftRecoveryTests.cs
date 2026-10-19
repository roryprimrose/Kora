using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed class ConversationDraftRecoveryTests
{
    [Theory]
    [InlineData("old", 4, "old")]
    [InlineData("old", 5, "latest")]
    [InlineData("old", 5, null)]
    [InlineData(null, 4, null)]
    [InlineData(null, 5, "")]
    [InlineData("", 5, null)]
    public async Task ExplicitReviewReturnsOnlyExactRereadBaselineAndNeverTransformsRawBuffer(string? old, long revision, string? latestBody)
    {
        var original = Observed(old);
        var latest = original with { Draft = original.Draft with { Revision = revision, Body = latestBody } };
        const string raw = "  e\u0301\U0001f642\r\n unsaved \t ";
        var reads = 0;
        var reviews = 0;
        ConversationDraftRecoveryReview? retained = null;
        var result = await ConversationDraftRecovery.ReviewAsync(new(original.Session, null), original, raw,
            _ => { reads++; return Task.FromResult(latest); }, (review, _) =>
            {
                reviews++;
                retained = review;
                review.Available.Should().BeTrue();
                review.OldBody.Should().Be(old);
                review.LatestBody.Should().Be(latestBody);
                review.Buffer.Should().Be(raw);
                review.Summary.Should().Contain("old observed saved revision 4")
                    .And.Contain("latest observed saved revision " + revision)
                    .And.Contain("absent is not empty").And.Contain("no merge, Save, Submit")
                    .And.Contain(original.Session.SessionId.Value.ToString("D")).And.Contain("generation 3");
                return Task.FromResult(true);
            }, () => true, TestContext.Current.CancellationToken);
        result.Should().Be(latest);
        reads.Should().Be(2);
        reviews.Should().Be(1);
        original.Draft.Body.Should().Be(old);
        retained!.Available.Should().BeFalse();
        retained.OldBody.Should().BeNull();
        retained.LatestBody.Should().BeNull();
        retained.Buffer.Should().BeEmpty();
        retained.Summary.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationOfReviewDoesNotRereadOrReturnAdoption()
    {
        var original = Observed("old");
        var reads = 0;
        var result = await ConversationDraftRecovery.ReviewAsync(new(original.Session, null), original, "raw",
            _ => { reads++; return Task.FromResult(original); },
            (_, _) => Task.FromResult(false), () => true, TestContext.Current.CancellationToken);
        result.Should().BeNull();
        reads.Should().Be(1);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("revision")]
    [InlineData("aba")]
    [InlineData("generation")]
    [InlineData("session")]
    [InlineData("removed")]
    [InlineData("metadata")]
    [InlineData("corrupt")]
    [InlineData("read-failure")]
    public async Task ConfirmationRefusesEveryChangedReadbackWithoutFollowingIt(string fault)
    {
        var original = Observed("old");
        var latest = original with { Draft = original.Draft with { Revision = 5, Body = "latest" } };
        var changed = fault switch
        {
            "body" => latest with { Draft = latest.Draft with { Body = "different at same revision" } },
            "revision" => latest with { Draft = latest.Draft with { Revision = 6 } },
            "aba" => latest with { Draft = latest.Draft with { Revision = 7 } },
            "generation" => latest with { Session = latest.Session with { Generation = new(4) } },
            "session" => latest with { Session = latest.Session with { SessionId = new(Guid.NewGuid()) } },
            "removed" => latest with { Draft = latest.Draft with { Revision = 6, Body = null } },
            "metadata" => latest with { ExpectedTarget = new(latest.Session, new(latest.Session.SessionId, new(1), new("changed"))) },
            "corrupt" => latest with { Draft = latest.Draft with { Version = 2 } },
            _ => latest,
        };
        var reads = 0;
        ConversationDraftRecoveryReview? retained = null;
        var operation = () => ConversationDraftRecovery.ReviewAsync(new(original.Session, null), original, "raw",
            _ =>
            {
                reads++;
                if (reads == 2 && fault is "read-failure") { throw new IOException("Readback unavailable."); }
                return Task.FromResult(reads == 1 ? latest : changed);
            }, (review, _) => { retained = review; return Task.FromResult(true); },
            () => true, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<Exception>();
        reads.Should().Be(2);
        retained!.Available.Should().BeFalse();
        retained.Buffer.Should().BeEmpty();
        original.Draft.Body.Should().Be("old");
    }

    [Theory]
    [InlineData("before")]
    [InlineData("read")]
    [InlineData("review")]
    [InlineData("cancel-review")]
    [InlineData("readback")]
    [InlineData("dispose")]
    [InlineData("token")]
    public async Task RevocationAtEveryAwaitBoundaryNeverReturnsAnAdoption(string boundary)
    {
        var original = Observed("private old");
        var admitted = boundary is not "before";
        var reads = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var operation = () => ConversationDraftRecovery.ReviewAsync(new(original.Session, null), original, "private raw",
            _ =>
            {
                reads++;
                if (boundary is "read" || boundary is "readback" && reads == 2) { admitted = false; }
                return Task.FromResult(original);
            }, (review, _) =>
            {
                if (boundary is "review" or "cancel-review") { admitted = false; }
                if (boundary is "token") { cancellation.Cancel(); }
                if (boundary is "dispose") { review.Dispose(); }
                return Task.FromResult(boundary is not "cancel-review");
            }, () => admitted, cancellation.Token);
        await operation.Should().ThrowAsync<OperationCanceledException>();
        reads.Should().Be(boundary is "before" ? 0 : boundary is "readback" ? 2 : 1);
    }

    [Theory]
    [InlineData("old")]
    [InlineData("latest")]
    [InlineData("raw")]
    public async Task CompleteTextPolicyRejectsEveryInvalidSideWithoutTruncation(string side)
    {
        foreach (var invalid in new[]
        {
            new string('x', ConversationText.MaximumUtf16Characters + 1),
            new string('界', ConversationText.MaximumUtf8Bytes / 3 + 1),
            "\ud800", "safe\u0000unsafe", "safe\ufffe", "api_key='synthetic-value-123'",
        })
        {
            var original = Observed(side is "old" ? invalid : "old");
            var latest = original with { Draft = original.Draft with { Revision = 5, Body = side is "latest" ? invalid : "latest" } };
            var raw = side is "raw" ? invalid : "raw";
            var reviews = 0;
            var reads = 0;
            var operation = () => ConversationDraftRecovery.ReviewAsync(new(original.Session, null), original, raw,
                _ => { reads++; return Task.FromResult(latest); }, (_, _) => { reviews++; return Task.FromResult(true); },
                () => true, TestContext.Current.CancellationToken);
            await operation.Should().ThrowAsync<Exception>();
            reviews.Should().Be(0);
            reads.Should().Be(side is "latest" ? 1 : 0, "invalid old/raw input never triggers a hidden source read");
        }
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("generation")]
    [InlineData("draft-session")]
    [InlineData("expected-target")]
    [InlineData("regressed")]
    [InlineData("same-revision-body")]
    [InlineData("version")]
    [InlineData("revision")]
    public async Task UnknownOriginalOrLatestAuthorityNeverReachesReview(string fault)
    {
        var original = Observed("old");
        var target = new SessionWorkspaceEntry(original.Session, null);
        var latest = original;
        switch (fault)
        {
            case "inactive": target = target with { Authority = target.Authority with { IsActive = false } }; break;
            case "generation": target = target with { Authority = target.Authority with { Generation = default } }; break;
            case "draft-session": original = original with { Draft = original.Draft with { SessionId = new(Guid.NewGuid()) } }; break;
            case "expected-target": original = original with { ExpectedTarget = target with { Metadata = new(target.Authority.SessionId, new(1), new("different")) } }; break;
            case "regressed": latest = latest with { Draft = latest.Draft with { Revision = 3 } }; break;
            case "same-revision-body": latest = latest with { Draft = latest.Draft with { Body = "replacement" } }; break;
            case "version": original = original with { Draft = original.Draft with { Version = 2 } }; break;
            case "revision": original = original with { Draft = original.Draft with { Revision = 0 } }; break;
        }
        var operation = () => ConversationDraftRecovery.ReviewAsync(target, original, "raw",
            _ => Task.FromResult(latest), (_, _) => throw new InvalidOperationException("Must not review invalid authority."),
            () => true, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task MaximumExactTextAndValidExpectedTargetAreRetained()
    {
        var raw = new string('界', ConversationText.MaximumUtf16Characters - 6) + " \r\n\U0001f642 ";
        var original = Observed(raw);
        var target = new SessionWorkspaceEntry(original.Session, null);
        original = original with { ExpectedTarget = target };
        var result = await ConversationDraftRecovery.ReviewAsync(target, original, raw, _ => Task.FromResult(original),
            (review, _) =>
            {
                review.OldBody.Should().Be(raw);
                review.LatestBody.Should().Be(raw);
                review.Buffer.Should().Be(raw);
                return Task.FromResult(true);
            }, () => true, TestContext.Current.CancellationToken);
        result.Should().Be(original);
    }

    [Fact]
    public async Task RetiringReviewWhileConfirmationReadIsPendingCannotReturnAdoption()
    {
        var original = Observed("old");
        ConversationDraftRecoveryReview? retained = null;
        var reads = 0;
        var operation = () => ConversationDraftRecovery.ReviewAsync(new(original.Session, null), original, "raw",
            _ =>
            {
                if (++reads == 2) { retained!.Dispose(); }
                return Task.FromResult(original);
            }, (review, _) => { retained = review; return Task.FromResult(true); }, () => true, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<OperationCanceledException>();
        reads.Should().Be(2);
        retained!.Available.Should().BeFalse();
    }

    [Fact]
    public void ReviewConstructorRejectsAlreadyRevokedAdmissionBeforePublishingPrivateFields()
    {
        var original = Observed("old");
        var create = () => new ConversationDraftRecoveryReview(new(original.Session, null), original, original, "raw", () => false);
        create.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData("available")]
    [InlineData("old")]
    [InlineData("latest")]
    [InlineData("buffer")]
    [InlineData("summary")]
    [InlineData("dispose")]
    public void EveryReviewAccessErasesRevokedPrivateDataWithoutResurrection(string first)
    {
        var original = Observed("private old");
        var admitted = true;
        using var review = new ConversationDraftRecoveryReview(new(original.Session, null), original, original, "private raw", () => admitted);
        admitted = false;
        switch (first)
        {
            case "available": review.Available.Should().BeFalse(); break;
            case "old": review.OldBody.Should().BeNull(); break;
            case "latest": review.LatestBody.Should().BeNull(); break;
            case "buffer": review.Buffer.Should().BeEmpty(); break;
            case "summary": review.Summary.Should().BeEmpty(); break;
            case "dispose": review.Dispose(); break;
        }
        admitted = true;
        review.Available.Should().BeFalse();
        review.OldBody.Should().BeNull();
        review.LatestBody.Should().BeNull();
        review.Buffer.Should().BeEmpty();
        review.Summary.Should().BeEmpty();
    }

    [Theory]
    [InlineData("target")]
    [InlineData("original")]
    [InlineData("read")]
    [InlineData("review")]
    [InlineData("current")]
    [InlineData("raw")]
    public async Task MissingWorkflowInputsAreExplicitFailures(string missing)
    {
        var original = Observed("old");
        var operation = () => ConversationDraftRecovery.ReviewAsync(missing is "target" ? null! : new(original.Session, null),
            missing is "original" ? null! : original, missing is "raw" ? null! : "raw",
            missing is "read" ? null! : _ => Task.FromResult(original),
            missing is "review" ? null! : (_, _) => Task.FromResult(true),
            missing is "current" ? null! : () => true, TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  exact e\u0301\U0001f642\r\n\t ")]
    public void SharedSaveReceiptCheckRequiresExactIdentityRevisionAndBodyIncludingAbsentVersusEmpty(string? body)
    {
        var expected = Observed("old");
        var receipt = expected with { Draft = expected.Draft with { Revision = 5, Body = body } };
        var verify = () => SessionConversationService.RequireDraftSaveReceipt(expected, body, receipt);
        verify.Should().NotThrow();
        var target = new SessionWorkspaceEntry(expected.Session, null);
        expected = expected with { ExpectedTarget = target };
        receipt = receipt with { ExpectedTarget = target };
        verify.Should().NotThrow();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("target")]
    [InlineData("draft-session")]
    [InlineData("revision")]
    [InlineData("body")]
    [InlineData("absent")]
    [InlineData("version")]
    public void MalformedOrMismatchedSaveReceiptNeverBecomesConfirmedRecoveryAuthority(string fault)
    {
        var expected = Observed("old");
        var receipt = expected with { Draft = expected.Draft with { Revision = 5, Body = string.Empty } };
        receipt = fault switch
        {
            "session" => receipt with { Session = receipt.Session with { SessionId = new(Guid.NewGuid()) } },
            "generation" => receipt with { Session = receipt.Session with { Generation = new(4) } },
            "target" => receipt with { ExpectedTarget = new(receipt.Session, null) },
            "draft-session" => receipt with { Draft = receipt.Draft with { SessionId = new(Guid.NewGuid()) } },
            "revision" => receipt with { Draft = receipt.Draft with { Revision = 6 } },
            "body" => receipt with { Draft = receipt.Draft with { Body = "changed" } },
            "absent" => receipt with { Draft = receipt.Draft with { Body = null } },
            "version" => receipt with { Draft = receipt.Draft with { Version = 2 } },
            _ => receipt,
        };
        var verify = () => SessionConversationService.RequireDraftSaveReceipt(expected, string.Empty, receipt);
        if (fault is "version") { verify.Should().Throw<InvalidDataException>(); }
        else { verify.Should().Throw<IOException>(); }
    }

    private static ConversationDraftSnapshot Observed(string? body)
    {
        var session = new WorkSessionAuthorization(new HostId<SessionIdentity>(Guid.NewGuid()), new(3), true);
        return new(session, new(session.SessionId, 4, body));
    }
}
