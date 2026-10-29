using System.Text;
using AwesomeAssertions;
using Kora.Application.Context;
using Kora.Application.Diagnostics;
using Kora.Core.Context;
using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Context;

public sealed class LocalSourceUsePolicyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static LocalSourceUsePolicy Policy => LocalSourceUsePolicy.Instance;

    private static (LocalFileRevision File, LocalFileSearchResult Search) File(string text = "alpha original text")
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var file = new LocalFileRevision(new(Guid.NewGuid(), Guid.NewGuid(), HostRequest.Create(RequestOrigin.LocalUi),
            new(@"C:\Team\guide.md", "identity", bytes.Length, DateTimeOffset.UnixEpoch)), bytes, DateTimeOffset.UnixEpoch);
        return (file, new LocalFileLexicalRetrieval().Search(file, file.Reference, "alpha", DateTimeOffset.UnixEpoch, Token));
    }

    [Theory]
    [InlineData("alpha original text", LocalFileModelUseDecision.Allowed)]
    [InlineData("alpha original text\napi_key=example", LocalFileModelUseDecision.Denied)]
    [InlineData("{alpha broken JSON", LocalFileModelUseDecision.Unavailable)]
    public async Task FilesUseWholeImmutableSourceAndExactCitations(string text, LocalFileModelUseDecision expected)
    {
        var (file, search) = File(text);
        (await Policy.AdmitAsync(file, "alpha?", search, Token)).Should().Be(expected);
    }

    [Fact]
    public async Task RisksOutsideSelectedExcerptsAndInOriginalQuestionCannotBeHidden()
    {
        var (file, search) = File("alpha selected\n" + new string('z', 2048) + "\napi_key=example");
        search.Citations.Should().NotBeEmpty().And.OnlyContain(item => !item.Excerpt.Contains("api_key", StringComparison.Ordinal));
        (await Policy.AdmitAsync(file, "alpha", search, Token)).Should().Be(LocalFileModelUseDecision.Denied);
        (file, search) = File();
        (await Policy.AdmitAsync(file, "alpha api_key=example", search, Token)).Should().Be(LocalFileModelUseDecision.Denied);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("outcome")]
    [InlineData("mode")]
    [InlineData("version")]
    [InlineData("empty")]
    [InlineData("many")]
    [InlineData("matching")]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    public async Task MissingForeignOrMalformedSelectionsNeverAdmitUse(string fault)
    {
        var (file, search) = File();
        var question = fault is "question" ? "!!!" : "alpha";
        search = fault switch
        {
            "outcome" => search with { Outcome = LocalFileSearchOutcome.NoMatch },
            "mode" => search with { Mode = (LocalFileRetrievalMode)999 },
            "version" => search with { PolicyVersion = "unknown" },
            "empty" => search with { Citations = [] },
            "many" => search with { Citations = Enumerable.Repeat(search.Citations[0], 9).ToArray() },
            "matching" => search with { MatchingChunks = 0 },
            "foreign" => File().Search,
            "duplicate" => search with { Citations = [search.Citations[0], search.Citations[0]], MatchingChunks = 2 },
            _ => search,
        };
        (await Policy.AdmitAsync(file, question, search, Token)).Should().Be(LocalFileModelUseDecision.Denied);
    }

    [Fact]
    public async Task CompleteExcerptByteBudgetIsNotSilentlyReduced()
    {
        var (file, search) = File(string.Join('\n', Enumerable.Range(0, 8).Select(index => $"alpha{index} " + new string('\u4e00', 1500))));
        var retrieval = new LocalFileLexicalRetrieval();
        var citations = Enumerable.Range(0, 8).SelectMany(index =>
            retrieval.Search(file, file.Reference, $"alpha{index}", DateTimeOffset.UnixEpoch, Token).Citations).ToArray();
        search = search with { Citations = citations, MatchingChunks = citations.Length };
        (await Policy.AdmitAsync(file, "alpha", search, Token)).Should().Be(LocalFileModelUseDecision.Denied);
    }

    [Theory]
    [InlineData("ordinary private prose", WebResultModelUseDecision.Allowed)]
    [InlineData("api_key=example", WebResultModelUseDecision.Denied)]
    [InlineData("{broken", WebResultModelUseDecision.Unavailable)]
    public async Task WebSourcesUseTheSameRiskPolicy(string text, WebResultModelUseDecision expected)
    {
        var snapshot = Snapshot(text, "https://example.com/");
        (await Policy.AdmitAsync(snapshot, Token)).Should().Be(expected);
    }

    [Fact]
    public async Task WebObservedAddressesAreScreenedAsInertPayloadNotAuthority() =>
        (await Policy.AdmitAsync(Snapshot("ordinary", "https://example.com/?api_key=example"), Token))
            .Should().Be(WebResultModelUseDecision.Denied);

    [Fact]
    public async Task AllPublicPolicySurfacesHonorCancellationAndRejectMissingInputs()
    {
        var (file, search) = File();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => Policy.AdmitAsync(file, "alpha", search, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => Policy.AdmitAsync(Snapshot("ordinary", "https://example.com/"), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => Policy.AdmitAsync(Review(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => Policy.AdmitAsync(null!, "alpha", search, Token)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => Policy.AdmitAsync(file, "alpha", null!, Token)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => Policy.AdmitAsync((WebResultSnapshot)null!, Token)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => Policy.AdmitAsync((EvidenceAnswerReview)null!, Token)).Should().ThrowAsync<ArgumentNullException>();
    }

    private static WebResultSnapshot Snapshot(string text, string address) =>
        WebResultSnapshot.Capture(new(new(Guid.NewGuid()), 1), HostRequest.Create(RequestOrigin.LocalUi), 1,
            new(address), new(WebPageGetOutcome.Succeeded, "retrieved", new(address), "text/plain", text),
            DateTimeOffset.UnixEpoch).WebResult!;

    internal static EvidenceAnswerReview Review(string body = "private diagnostic body", string question = "What record?")
    {
        var record = new Kora.Core.Diagnostics.EvidenceRecord(new(Kora.Core.Diagnostics.EvidenceSource.Log, new(Guid.NewGuid())),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1), Kora.Core.Diagnostics.EvidenceSegmentStatus.Present,
            null, null, null, null, "Information", "Example", 1, body,
            new Dictionary<string, Kora.Core.Diagnostics.EvidenceValue>(StringComparer.Ordinal), null, null, []);
        return new(Encoding.UTF8.GetString(DurableEvidenceQuery.Serialize(
            new(Kora.Core.Diagnostics.EvidencePageStatus.Available, [record], null, [], Kora.Core.Diagnostics.EvidencePage.StorageDisclosure))),
            question, HostRequest.Create(RequestOrigin.LocalUi), DateTimeOffset.UnixEpoch);
    }
}
