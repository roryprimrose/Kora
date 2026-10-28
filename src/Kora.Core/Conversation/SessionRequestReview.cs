using System.Text.Json.Serialization;
using System.Collections.Immutable;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Private original-user review data, not execution, question or approval authority.</summary>
public sealed record SessionRequestReview(Guid Id, SessionWorkspaceEntry Target, string Body,
    RequestOrigin Origin, DateTimeOffset ExpiresAt,
    SessionRequestReview.ProviderChoice Provider = SessionRequestReview.ProviderChoice.Default)
{
    public ImmutableArray<string> ClaudeTools { get; init; } = [];
    public FailedRequestRetrySource? FailedSource { get; init; }
    public Kora.Core.Providers.ProviderProfileTuple? RetryProviderProfile { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ProviderChoice>))]
    public enum ProviderChoice { Default, Copilot, Claude }
}
