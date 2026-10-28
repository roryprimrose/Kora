using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Authorization;

/// <summary>Exact observational predicates on recorded metadata, never applicability or session authority.</summary>
public sealed record ExactGrantQuery
{
    public ExactGrantQuery(OperationGrantScope? scope = null, OperationGrantStatus? status = null,
        string? actionId = null, HostId<SessionIdentity>? originatingSession = null,
        bool managementView = false, HostId<SessionIdentity>? managementSession = null, bool includeOnceHistory = false)
    {
        if (scope is { } s && !Enum.IsDefined(s) || status is { } t && !Enum.IsDefined(t))
        {
            throw new InvalidDataException("An exact-grant filter requires a known stored scope and status.");
        }
        if (actionId is not null) { InteractionValidation.Identifier(actionId); }
        originatingSession?.Validate();
        managementSession?.Validate();
        if (!managementView && (managementSession is not null || includeOnceHistory))
        { throw new InvalidDataException("A management session requires the bounded permission management view."); }
        Scope = scope;
        Status = status;
        ActionId = actionId;
        OriginatingSession = originatingSession;
        ManagementView = managementView;
        ManagementSession = managementSession;
        IncludeOnceHistory = includeOnceHistory;
        var canonical = "exact-grant-query-v1\n"
            + (scope is { } scopeValue ? ((int)scopeValue).ToString(CultureInfo.InvariantCulture) : "any") + "\n"
            + (status is { } statusValue ? ((int)statusValue).ToString(CultureInfo.InvariantCulture) : "any") + "\n"
            + (actionId ?? string.Empty) + "\n" + (originatingSession?.Value.ToString("D") ?? string.Empty);
        if (managementView) { canonical += "\nmanagement\n" + (managementSession?.Value.ToString("D") ?? "persistent"); }
        if (includeOnceHistory) { canonical += "\nwith-once-history"; }
        Digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static ExactGrantQuery Unfiltered { get; } = new();
    public OperationGrantScope? Scope { get; }
    public OperationGrantStatus? Status { get; }
    public string? ActionId { get; }
    public HostId<SessionIdentity>? OriginatingSession { get; }
    public string Digest { get; }
    public bool ManagementView { get; }
    public HostId<SessionIdentity>? ManagementSession { get; }
    public bool IncludeOnceHistory { get; }

    /// <summary>Parses native canonical values; only Any and empty optional text mean unset. Never trims or repairs input.</summary>
    public static ExactGrantQuery FromInput(string scope, string status, string actionId, string originatingSession)
    {
        OperationGrantScope? parsedScope = scope switch
        {
            "Any" => null,
            nameof(OperationGrantScope.Once) => OperationGrantScope.Once,
            nameof(OperationGrantScope.Session) => OperationGrantScope.Session,
            nameof(OperationGrantScope.Perpetual) => OperationGrantScope.Perpetual,
            _ => throw new InvalidDataException("Select Any or an exact stored grant scope."),
        };
        OperationGrantStatus? parsedStatus = status switch
        {
            "Any" => null,
            nameof(OperationGrantStatus.Active) => OperationGrantStatus.Active,
            nameof(OperationGrantStatus.Consumed) => OperationGrantStatus.Consumed,
            nameof(OperationGrantStatus.Revoked) => OperationGrantStatus.Revoked,
            _ => throw new InvalidDataException("Select Any or an exact stored grant status."),
        };
        ArgumentNullException.ThrowIfNull(actionId);
        ArgumentNullException.ThrowIfNull(originatingSession);
        HostId<SessionIdentity>? session = null;
        if (originatingSession.Length != 0)
        {
            if (originatingSession.Length != 36 || !Guid.TryParseExact(originatingSession, "D", out var id)
                || id == Guid.Empty || !string.Equals(originatingSession, id.ToString("D"), StringComparison.Ordinal))
            {
                throw new InvalidDataException("The originating session filter requires a nonempty canonical lowercase D GUID.");
            }
            session = new(id);
        }
        return new(parsedScope, parsedStatus, actionId.Length == 0 ? null : actionId, session);
    }

    public bool Matches(OperationGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return (!ManagementView || grant.Scope == OperationGrantScope.Perpetual
                || ManagementSession is { } target
                    && (grant.Scope == OperationGrantScope.Session || IncludeOnceHistory && grant.Scope == OperationGrantScope.Once)
                    && grant.ApprovedProposal.Request.SessionId == target)
            && (Scope is not { } scope || scope == grant.Scope)
            && (Status is not { } status || status == grant.Status)
            && (ActionId is null || string.Equals(ActionId, grant.ApprovedProposal.Binding.ActionId, StringComparison.Ordinal))
            && (OriginatingSession is not { } session || session == grant.ApprovedProposal.Request.SessionId);
    }
}
