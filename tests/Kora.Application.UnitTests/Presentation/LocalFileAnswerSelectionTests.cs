using System.Text;
using AwesomeAssertions;
using Kora.Application.Presentation;
using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Presentation;

public sealed class LocalFileAnswerSelectionTests
{
    [Theory]
    [InlineData("cancel")]
    [InlineData("query")]
    [InlineData("source")]
    [InlineData("close")]
    public async Task NativeGenerationFencesDispatchAndLateAnswers(string change)
    {
        using var selection = new LocalFileAnswerSelection();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<bool>? gate = null;
        CancellationToken captured = default;
        var current = true;
        var run = selection.Run("exact question", async (question, isCurrent, token) =>
        {
            question.Should().Be("exact question");
            gate = isCurrent;
            captured = token;
            await release.Task;
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Succeeded);
        }, () => current);
        gate!().Should().BeTrue();
        if (string.Equals(change, "source", StringComparison.Ordinal)) { current = false; }
        else if (string.Equals(change, "close", StringComparison.Ordinal)) { selection.Dispose(); }
        else { selection.Invalidate(); }
        gate().Should().BeFalse();
        captured.IsCancellationRequested.Should().Be(!string.Equals(change, "source", StringComparison.Ordinal));
        release.SetResult();
        (await run).Should().BeNull();
    }

    [Fact]
    public async Task OnlyCurrentAnswerOwnedCitationObjectsCanNavigateExactSource()
    {
        using var selection = new LocalFileAnswerSelection();
        var bytes = Encoding.UTF8.GetBytes("alpha\r\nexact marker");
        var source = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\guide.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
        var citation = new LocalFileLexicalRetrieval().Search(source, source.Reference, "alpha", DateTimeOffset.UnixEpoch,
            CancellationToken.None).Citations.Single();
        var response = new LocalFileAnswerResult(LocalFileAnswerOutcome.Succeeded, [new("marker", [citation])]);
        (await selection.Run("alpha", (_, gate, _) =>
        {
            gate().Should().BeTrue();
            return Task.FromResult(response);
        }, () => true)).Should().BeSameAs(response);
        selection.Resolve(citation, [source], () => true).Text.Should().Be(source.Text);
        var foreign = () => selection.Resolve(citation with { }, [source], () => true);
        foreign.Should().Throw<InvalidOperationException>();
        var missing = () => selection.Resolve(citation, [], () => true);
        missing.Should().Throw<InvalidOperationException>();
        var retired = () => selection.Resolve(citation, [source], () => false);
        retired.Should().Throw<InvalidOperationException>();
        selection.Invalidate();
        var stale = () => selection.Resolve(citation, [source], () => true);
        stale.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task OwnedCancellationDiscardsButUnexpectedFailureIsExplicit()
    {
        using var selection = new LocalFileAnswerSelection();
        (await selection.Run("alpha", (_, _, token) =>
        {
            selection.Invalidate();
            return Task.FromCanceled<LocalFileAnswerResult>(token);
        }, () => true)).Should().BeNull();
        var failed = () => selection.Run("alpha", (_, _, _) => Task.FromException<LocalFileAnswerResult>(new IOException()), () => true);
        await failed.Should().ThrowAsync<IOException>();
        selection.Dispose();
        await failed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReentrantSourceCheckCannotReviveRetiredNativeGeneration(bool dispose)
    {
        using var selection = new LocalFileAnswerSelection();
        (await selection.Run("alpha", (_, _, _) => Task.FromResult(LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Unsupported)),
            () =>
            {
                if (dispose) { selection.Dispose(); }
                else { selection.Invalidate(); }
                return true;
            })).Should().BeNull();
    }
}
