using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Private original-user review data, not execution, question or approval authority.</summary>
public sealed record SessionRequestReview(Guid Id, SessionWorkspaceEntry Target, string Body,
    RequestOrigin Origin, DateTimeOffset ExpiresAt);
