using System.ComponentModel;

using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Theory]
    [InlineData("", "insert", "literal approve; submit")]
    [InlineData("original\r\n", "append", "original\r\nliteral approve; submit")]
    [InlineData("original\r\n", "replace", "literal approve; submit")]
    [InlineData("original\r\n", "cancel", "original\r\n")]
    public async Task ExactSessionTargetUsesSharedLiteralOwnerWithoutTouchingNewRequest(string body, string decision, string expected)
    {
        await using var f = new DictationFixture();
        f.Host.Composer.Body = "independent New request";
        var source = new SessionBuffer(body);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        HostRequest? observed = null;
        var insertions = 0;
        f.Capture.BeforeEnd = () => observed = HostActivity.RequireCurrent().Request;
        var target = new ComposerDictationTarget(source, body, request, () => source.Current,
            (literal, admitted) =>
            {
                insertions++;
                if (!admitted()) { return false; }
                source.Body = literal;
                return admitted();
            });
        f.Service.IsActiveFor(target).Should().BeFalse();
        f.Service.IsActiveFor(f.Host.Composer).Should().BeFalse();
        await f.Service.BeginAsync(target, () => true, TestContext.Current.CancellationToken);
        f.Service.IsActiveFor(target).Should().BeTrue();
        f.Service.IsActiveFor(f.Host.Composer).Should().BeFalse();
        f.Capture.Transcript("literal approve; submit");
        source.Body.Should().Be(body);
        await f.Service.EndAsync();
        if (body.Length != 0)
        {
            f.Service.HasReview.Should().BeTrue();
            f.Service.OriginalBody.Should().Be(body);
            f.Service.LiteralText.Should().Be("literal approve; submit");
            if (decision is "append") { f.Service.Append(); }
            else if (decision is "replace") { f.Service.Replace(); }
            else { f.Service.Retire("Explicit Cancel."); }
        }
        await f.Service.Quiescence;
        source.Body.Should().Be(expected);
        f.Host.Composer.Body.Should().Be("independent New request");
        f.Service.IsActiveFor(target).Should().BeFalse();
        observed.Should().Be(request);
        insertions.Should().Be(decision is "cancel" ? 0 : 1);
        f.Capture.Phrases.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SessionComposerSubmitRunsOnceOnlyAfterConfirmedInsertion(bool submits, bool inserted)
    {
        await using var f = new DictationFixture();
        var source = new SessionBuffer("original");
        var submissions = 0;
        var bodyAtSubmit = string.Empty;
        var target = new ComposerDictationTarget(source, source.Body, HostRequest.Create(RequestOrigin.LocalUi),
            () => source.Current,
            (literal, admitted) =>
            {
                if (!inserted || !admitted()) { return false; }
                source.Body = literal;
                return true;
            },
            ComposerDictationDestination.SessionComposer,
            submits ? () => { submissions++; bodyAtSubmit = source.Body; } : null);
        await f.Service.BeginAsync(target, () => true, TestContext.Current.CancellationToken);
        f.Capture.Transcript("dictated");
        await f.Service.EndAsync();
        submissions.Should().Be(0, "review is not submission");
        f.Service.Append();
        await f.Service.Quiescence;
        submissions.Should().Be(submits && inserted ? 1 : 0);
        bodyAtSubmit.Should().Be(submits && inserted ? "originaldictated" : string.Empty);
        source.Body.Should().Be(inserted ? "originaldictated" : "original");
        f.Service.Status.Should().Be(!inserted ? "Dictation insertion revoked. Original or newer independent memory preserved."
            : submits ? "Literal text inserted and submitted through the session's single Submit."
            : "Literal text inserted into private draft memory only. No Create, Save, Submit, answer or execution.");
        f.Service.Append();
        submissions.Should().Be(submits && inserted ? 1 : 0, "a consumed review cannot submit again");
        if (!submits)
        {
            target.Submit();
            source.Body.Should().Be(inserted ? "originaldictated" : "original", "a target without a submit callback never submits");
        }
    }

    [Fact]
    public async Task ExactTargetSourceEventRevokesReviewAndNeverPublishesLateText()
    {
        await using var f = new DictationFixture();
        var source = new SessionBuffer("old");
        var insertions = 0;
        var target = new ComposerDictationTarget(source, source.Body, HostRequest.Create(RequestOrigin.LocalUi),
            () => source.Current, (_, _) => { insertions++; return true; });
        source.Current = false;
        await f.Service.BeginAsync(target, () => true, TestContext.Current.CancellationToken);
        f.Capture.Opens.Should().Be(0);
        source.Current = true;
        await f.Service.BeginAsync(target, () => true, TestContext.Current.CancellationToken);
        var generation = f.Capture.Generation;
        f.Capture.Transcript("late");
        await f.Service.EndAsync();
        f.Service.HasReview.Should().BeTrue();
        source.Revoke();
        f.Service.HasReview.Should().BeFalse();
        f.Capture.Transcript("late callback", generation);
        f.Service.Append();
        f.Service.Replace();
        await f.Service.Quiescence;
        insertions.Should().Be(0);
        source.Body.Should().Be("old");
    }

    private sealed class SessionBuffer(string body) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        internal string Body { get; set; } = body;
        internal bool Current { get; set; } = true;
        internal void Revoke()
        {
            Current = false;
            PropertyChanged?.Invoke(this, new(nameof(Body)));
        }
    }
}
