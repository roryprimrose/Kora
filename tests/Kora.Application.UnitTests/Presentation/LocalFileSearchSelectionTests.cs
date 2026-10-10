using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Context;

namespace Kora.Application.UnitTests.Presentation;

public sealed class LocalFileSearchSelectionTests
{
    [Fact]
    public async Task RunDefaultsToOriginalPolicyAndOptInIsRunOnlyWithExactUnmodifiedQuery()
    {
        using var selection = new LocalFileSearchSelection();
        selection.Mode.Should().Be(LocalFileRetrievalMode.LexicalLines);
        const string query = "original café \r\n alpha";
        LocalFileRetrievalMode? observed = null;
        Task<LocalFileSearchResult> Search(string actual, LocalFileRetrievalMode mode, CancellationToken token)
        {
            actual.Should().Be(query);
            token.IsCancellationRequested.Should().BeFalse();
            observed = mode;
            return Task.FromResult(LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode));
        }
        (await selection.Run(query, Search, () => true))!.PolicyVersion.Should().Be("lexical-lines-v1");
        observed.Should().Be(LocalFileRetrievalMode.LexicalLines);
        selection.Select(LocalFileRetrievalMode.MarkdownStructure);
        (await selection.Run(query, Search, () => true))!.PolicyVersion.Should().Be("markdown-structure-v2");
        observed.Should().Be(LocalFileRetrievalMode.MarkdownStructure);
        using var reopened = new LocalFileSearchSelection();
        reopened.Mode.Should().Be(LocalFileRetrievalMode.LexicalLines);
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("query")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("dispose")]
    [InlineData("next")]
    public async Task RunCancelsAndDiscardsLateWorkAfterSelectionQuerySourceOrLifecycleChanges(string change)
    {
        using var selection = new LocalFileSearchSelection();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eligible = true;
        CancellationToken captured = default;
        var old = selection.Run("old query", async (_, mode, token) =>
        {
            captured = token;
            entered.SetResult();
            await release.Task;
            return LocalFileSearchResult.Empty(LocalFileSearchOutcome.Matched, DateTimeOffset.UnixEpoch, mode);
        }, () => eligible);
        await entered.Task;
        switch (change)
        {
            case "mode": selection.Select(LocalFileRetrievalMode.MarkdownStructure); break;
            case "query":
            case "cancel": selection.Invalidate(); break;
            case "source": eligible = false; break;
            case "dispose": selection.Dispose(); break;
            default:
                (await selection.Run("new", (_, mode, _) => Task.FromResult(
                    LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)), () => true))
                    .Should().NotBeNull();
                break;
        }
        captured.IsCancellationRequested.Should().Be(!string.Equals(change, "source", StringComparison.Ordinal));
        release.SetResult();
        (await old).Should().BeNull();
    }

    [Fact]
    public async Task RunSuppressesOwnedCancellationButPropagatesFailuresAndNeverFallsBackFromUnknownPolicy()
    {
        using var selection = new LocalFileSearchSelection();
        var cancelled = await selection.Run("query", (_, _, token) =>
        {
            selection.Invalidate();
            return Task.FromCanceled<LocalFileSearchResult>(token);
        }, () => true);
        cancelled.Should().BeNull();
        var failed = () => selection.Run("query", (_, _, _) => Task.FromException<LocalFileSearchResult>(
            new IOException("unavailable")), () => true);
        await failed.Should().ThrowAsync<IOException>();
        selection.Select(LocalFileRetrievalMode.MarkdownStructure);
        var mismatched = () => selection.Run("query", (_, _, _) => Task.FromResult(
            LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch)), () => true);
        await mismatched.Should().ThrowAsync<InvalidOperationException>();
        var version = () => selection.Run("query", (_, mode, _) => Task.FromResult(
            LocalFileSearchResult.Empty(LocalFileSearchOutcome.NoMatch, DateTimeOffset.UnixEpoch, mode)
                with { PolicyVersion = "unknown-future-version" }), () => true);
        await version.Should().ThrowAsync<InvalidOperationException>();
        var select = () => selection.Select((LocalFileRetrievalMode)88);
        select.Should().Throw<InvalidOperationException>();
        var unknown = () => selection.Run("query", (_, _, _) => throw new InvalidOperationException("must not execute"), () => true);
        await unknown.Should().ThrowAsync<InvalidOperationException>().WithMessage("*supported retrieval mode*");
        selection.Dispose();
        await unknown.Should().ThrowAsync<ObjectDisposedException>();
        var closed = () => selection.Select(LocalFileRetrievalMode.LexicalLines);
        closed.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("source")]
    [InlineData("dispose")]
    public async Task RunDiscardsLateFailureWithoutClearingOrReportingAgainstNewSelection(string change)
    {
        using var selection = new LocalFileSearchSelection();
        var current = true;
        var result = await selection.Run("query", (_, _, _) =>
        {
            if (string.Equals(change, "mode", StringComparison.Ordinal)) { selection.Select(LocalFileRetrievalMode.MarkdownStructure); }
            else if (string.Equals(change, "source", StringComparison.Ordinal)) { current = false; }
            else { selection.Dispose(); }
            return Task.FromException<LocalFileSearchResult>(new IOException("old failure"));
        }, () => current);
        result.Should().BeNull();
    }
}
