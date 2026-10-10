using Kora.Core.Auditing;
using Kora.Core.Network;

namespace Kora.Application.Configuration;

public sealed class PreapprovedUriConfigurationService(
    IPreapprovedUriPreferences preferences,
    ISecurityAuditLog auditLog) : IPreapprovedUriConfiguration
{
    private const string AuditAction = "configuration.preapproved-uri";
    private const string AuditTarget = "preferences.device-local";
    private readonly Lock gate = new();
    private PreapprovedUriSettings settings = PreapprovedUriSettings.Empty;
    private bool loaded;

    public event EventHandler? Changed;

    public PreapprovedUriSettings GetSettings()
    {
        lock (gate)
        {
            EnsureLoaded();
            return settings;
        }
    }

    public bool IsPreapproved(Uri uri)
    {
        lock (gate)
        {
            EnsureLoaded();
            return settings.IsPreapproved(uri);
        }
    }

    public PreapprovedUriSettings Add(string pattern, SecurityAuditInitiator initiator)
    {
        ValidateInitiator(initiator);
        lock (gate)
        {
            EnsureLoaded();
            var parsed = PreapprovedUriPattern.Parse(pattern).Value;
            if (settings.Patterns.Contains(parsed, StringComparer.Ordinal))
            {
                return settings;
            }
            return Commit(PreapprovedUriSettings.Create(settings.Patterns.Append(parsed)), initiator);
        }
    }

    public PreapprovedUriSettings Remove(string pattern, SecurityAuditInitiator initiator)
    {
        ValidateInitiator(initiator);
        lock (gate)
        {
            EnsureLoaded();
            var parsed = PreapprovedUriPattern.Parse(pattern).Value;
            if (!settings.Patterns.Contains(parsed, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("The URI pattern is not currently preapproved.");
            }
            return Commit(
                PreapprovedUriSettings.Create(settings.Patterns.Where(item =>
                    !string.Equals(item, parsed, StringComparison.Ordinal))),
                initiator);
        }
    }

    public PreapprovedUriSettings Clear(SecurityAuditInitiator initiator)
    {
        ValidateInitiator(initiator);
        lock (gate)
        {
            EnsureLoaded();
            return settings.Patterns.Count == 0
                ? settings
                : Commit(PreapprovedUriSettings.Empty, initiator);
        }
    }

    private PreapprovedUriSettings Commit(
        PreapprovedUriSettings next,
        SecurityAuditInitiator initiator)
    {
        var audit = new SecurityAuditEvent(
            Guid.NewGuid(),
            SecurityAuditCategory.ConfigurationWrite,
            AuditAction,
            SecurityAuditOutcome.Requested,
            initiator,
            AuditTarget);
        auditLog.Write(audit);
        try
        {
            preferences.Save(next);
            var readBack = preferences.Load();
            if (!readBack.Patterns.SequenceEqual(next.Patterns, StringComparer.Ordinal))
            {
                throw new InvalidDataException("The saved preapproved URI patterns did not match their readback.");
            }
            settings = readBack;
            loaded = true;
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
            Changed?.Invoke(this, EventArgs.Empty);
            return settings;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            loaded = false;
            auditLog.Write(audit.WithOutcome(
                SecurityAuditOutcome.Failed,
                exception is UnauthorizedAccessException ? "access-denied"
                    : exception is IOException ? "io-error" : "invalid-data"));
            throw;
        }
    }

    private void EnsureLoaded()
    {
        if (!loaded)
        {
            settings = preferences.Load();
            loaded = true;
        }
    }

    private static void ValidateInitiator(SecurityAuditInitiator initiator)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser
            or SecurityAuditInitiator.TypedCommand
            or SecurityAuditInitiator.VoiceCommand))
        {
            throw new InvalidOperationException(
                "Preapproved URI settings require an original local user action.");
        }
    }
}
