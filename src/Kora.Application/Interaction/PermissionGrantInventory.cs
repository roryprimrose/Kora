using System.Collections.Immutable;

using Kora.Core.Authorization;

namespace Kora.Application.Interaction;

public sealed record PermissionGrantInventory(ExactGrantPage ExactPage, ImmutableArray<PermissionGrantRecord> Grants);
