using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OllamaSharp;
using OllamaSharp.Models;

namespace Kora.Windows.Dependencies;

internal static class OllamaSdkMetadata
{
    private static readonly DefaultJsonTypeInfoResolver MetadataTypes = CreateMetadataTypes();

    internal static async Task VerifyAsync(string version, string model, string digest,
        Func<HttpRequestMessage, CancellationToken, Task<byte[]>> send, CancellationToken token)
    {
        using var http = new HttpClient(new MetadataHandler(version, model, digest, send))
        {
            BaseAddress = WindowsOllamaToolTransport.Endpoint,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        using var sdk = new OllamaApiClient(http);
        sdk.IncomingJsonSerializerOptions.TypeInfoResolver = MetadataTypes;
        try
        {
            var observedVersion = await sdk.GetVersionAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!string.Equals(observedVersion, version, StringComparison.Ordinal))
            { throw new InvalidDataException("SDK runtime projection differs from the admitted version."); }
            var models = await sdk.ListLocalModelsAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var selected = models.SingleOrDefault(item => string.Equals(item.Name, model, StringComparison.Ordinal));
            var observedDigest = selected?.Digest;
            if (observedDigest?.StartsWith("sha256:", StringComparison.Ordinal) == true)
            { observedDigest = observedDigest[7..]; }
            if (!string.Equals(observedDigest, digest, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("SDK model projection differs from the admitted digest."); }
        }
        catch (JsonException exception)
        { throw new InvalidDataException("The SDK could not decode the admitted Ollama metadata.", exception); }
    }

    private static DefaultJsonTypeInfoResolver CreateMetadataTypes()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(static type =>
        {
            if (type.Type != typeof(Model)) { return; }
            // The host validates this optional string but does not consume or interpret dates.
            var modified = type.Properties.Single(property => property.Name is "modified_at");
            type.Properties.Remove(modified);
        });
        return resolver;
    }

    private sealed class MetadataHandler(string version, string model, string digest,
        Func<HttpRequestMessage, CancellationToken, Task<byte[]>> send) : HttpMessageHandler
    {
        private int requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = requests switch { 0 => "api/version", 1 => "api/tags", _ => null };
            if (path is null || request.Method != HttpMethod.Get || request.Content is not null
                || request.Headers.Any() || request.RequestUri != new Uri(WindowsOllamaToolTransport.Endpoint, path))
            { throw new InvalidDataException("Only the exact read-only SDK metadata sequence is admitted."); }
            requests++;
            using var physical = new HttpRequestMessage(HttpMethod.Get, request.RequestUri);
            var bytes = await send(physical, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (requests == 1) { OllamaToolWire.VerifyVersion(bytes, version); }
            else { OllamaToolWire.VerifyModel(bytes, model, digest); }
            return new(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
                },
            };
        }
    }
}
