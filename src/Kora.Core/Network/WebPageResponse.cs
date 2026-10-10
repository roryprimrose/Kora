namespace Kora.Core.Network;

public sealed class WebPageResponse(
    int statusCode,
    Uri address,
    string? mediaType,
    string? characterSet,
    long? contentLength,
    Uri? redirectAddress,
    IReadOnlyList<string> contentEncodings,
    Stream content,
    IDisposable? owner = null) : IAsyncDisposable, IDisposable
{
    public int StatusCode { get; } = statusCode;
    public Uri Address { get; } = address;
    public string? MediaType { get; } = mediaType;
    public string? CharacterSet { get; } = characterSet;
    public long? ContentLength { get; } = contentLength;
    public Uri? RedirectAddress { get; } = redirectAddress;
    public IReadOnlyList<string> ContentEncodings { get; } = contentEncodings;
    public Stream Content { get; } = content;

    public void Dispose()
    {
        Content.Dispose();
        owner?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);
        owner?.Dispose();
    }
}
