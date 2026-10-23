using System.Net;
using AwesomeAssertions;
using Kora.Testing;
using Kora.Windows.Dependencies;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class HttpSpeechComponentAcquisitionTests
{
    [Fact]
    public async Task ExactHostDataUsesOneRequestAndDoesNotOwnTheDestinationStream()
    {
        var asset = SpeechComponentFixture.Asset("model.data");
        using var handler = new Handler(request => new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(SpeechComponentFixture.Bytes),
        });
        using var acquisition = new HttpSpeechComponentAcquisition(new HttpMessageInvoker(handler, disposeHandler: false));
        await using var destination = new MemoryStream();
        await acquisition.DownloadAsync(asset, destination, TestContext.Current.CancellationToken);
        destination.ToArray().Should().Equal(SpeechComponentFixture.Bytes);
        destination.CanWrite.Should().BeTrue();
        handler.Requests.Should().Equal(asset.Source);
        using var production = HttpSpeechComponentAcquisition.CreateDefault();
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RedirectsAndHttpFailuresNeverFollowAnotherSource(HttpStatusCode status)
    {
        using var handler = new Handler(request =>
        {
            var response = new HttpResponseMessage(status) { RequestMessage = request };
            response.Headers.Location = new Uri("https://other.example.invalid/model.data");
            return response;
        });
        using var acquisition = new HttpSpeechComponentAcquisition(new HttpMessageInvoker(handler, disposeHandler: false));
        await using var destination = new MemoryStream();
        await FluentActions.Awaiting(() => acquisition.DownloadAsync(SpeechComponentFixture.Asset("model.data"), destination,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
        destination.Length.Should().Be(0);
    }

    [Theory]
    [InlineData("changed-source")]
    [InlineData("declared-short")]
    [InlineData("stream-short")]
    [InlineData("stream-long")]
    public async Task UnexpectedSourceOrLengthIsRejectedWithoutUnboundedTransfer(string failure)
    {
        using var handler = new Handler(request => new(HttpStatusCode.OK)
        {
            RequestMessage = failure is "changed-source"
                ? new HttpRequestMessage(HttpMethod.Get, "https://other.example.invalid/model.data") : request,
            Content = failure is "declared-short" ? new ByteArrayContent([1])
                : new UnknownLengthContent(failure is "stream-long" ? [1, 2, 3, 4, 5]
                    : failure is "stream-short" ? [1, 2, 3] : SpeechComponentFixture.Bytes),
        });
        using var acquisition = new HttpSpeechComponentAcquisition(new HttpMessageInvoker(handler, disposeHandler: false));
        await using var destination = new MemoryStream();
        await FluentActions.Awaiting(() => acquisition.DownloadAsync(SpeechComponentFixture.Asset("model.data"), destination,
            TestContext.Current.CancellationToken)).Should().ThrowAsync<InvalidDataException>();
        destination.Length.Should().BeLessThanOrEqualTo(4);
        handler.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportCancellationDistinguishesCallerCancellationFromTimeout(bool callerCancelled)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        if (callerCancelled) { cancellation.Cancel(); }
        using var handler = new Handler(_ => throw new OperationCanceledException());
        using var acquisition = new HttpSpeechComponentAcquisition(new HttpMessageInvoker(handler, disposeHandler: false));
        await using var destination = new MemoryStream();
        if (callerCancelled)
        {
            await FluentActions.Awaiting(() => acquisition.DownloadAsync(SpeechComponentFixture.Asset("model.data"),
                destination, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await FluentActions.Awaiting(() => acquisition.DownloadAsync(SpeechComponentFixture.Asset("model.data"),
                destination, cancellation.Token)).Should().ThrowAsync<TimeoutException>();
        }
        destination.Length.Should().Be(0);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal List<Uri?> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes, TestContext.Current.CancellationToken).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
