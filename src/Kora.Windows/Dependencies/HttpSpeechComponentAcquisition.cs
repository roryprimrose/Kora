using System.Buffers;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

public sealed class HttpSpeechComponentAcquisition(HttpMessageInvoker transport) : ISpeechComponentAcquisition, IDisposable
{
    public static HttpSpeechComponentAcquisition CreateDefault() =>
        new(new HttpMessageInvoker(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }));

    public async Task DownloadAsync(SpeechComponentAsset asset, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(destination);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            await TransferAsync(asset, destination, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The exact speech component transfer timed out. Inspect before an explicit retry.", exception);
        }
    }

    private async Task TransferAsync(SpeechComponentAsset asset, Stream destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Source);
        using var response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri != asset.Source
            || response.Content.Headers.ContentLength is { } length && length != asset.SizeBytes)
        {
            throw new InvalidDataException("The exact component source or declared transfer length changed.");
        }
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long received = 0;
            int count;
            while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                received = checked(received + count);
                if (received > asset.SizeBytes) { throw new InvalidDataException("Component transfer exceeded its exact byte budget."); }
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            if (received != asset.SizeBytes) { throw new InvalidDataException("Component transfer was incomplete."); }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    public void Dispose() => transport.Dispose();
}
