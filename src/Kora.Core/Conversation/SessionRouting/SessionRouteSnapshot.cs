using System.Collections.Immutable;

namespace Kora.Core.Conversation.SessionRouting;

public sealed record SessionRouteSnapshot(long Revision, ImmutableArray<SessionRouteDescriptor> Descriptors);
