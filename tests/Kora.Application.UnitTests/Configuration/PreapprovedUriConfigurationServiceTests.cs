using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Network;

namespace Kora.Application.UnitTests.Configuration;

public sealed class PreapprovedUriConfigurationServiceTests
{
    [Fact]
    public void OriginalUserChangesAreAuditedAndImmediatelyAffectMatching()
    {
        var preferences = new Preferences();
        var audit = new Audit();
        var service = new PreapprovedUriConfigurationService(preferences, audit);

        service.Add("https://*.example.com/*", SecurityAuditInitiator.VoiceCommand);

        service.IsPreapproved(new Uri("https://api.example.com/data")).Should().BeTrue();
        service.IsPreapproved(new Uri("https://example.com/data")).Should().BeFalse();
        preferences.Saved.Patterns.Should().Equal("https://*.example.com/*");
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(item =>
            item.Category == SecurityAuditCategory.ConfigurationWrite
            && item.ActionId == "configuration.preapproved-uri");
    }

    [Fact]
    public void ModelCannotChangeItsOwnPreapprovalPolicy()
    {
        var service = new PreapprovedUriConfigurationService(new Preferences(), new Audit());

        service.Invoking(item => item.Add(
                "https://example.com/*",
                SecurityAuditInitiator.ModelSuggestion))
            .Should().Throw<InvalidOperationException>();
        service.GetSettings().Patterns.Should().BeEmpty();
    }

    [Fact]
    public void FailedReadBackDoesNotPublishSuccessShapedState()
    {
        var preferences = new Preferences { CorruptReadBack = true };
        var audit = new Audit();
        var service = new PreapprovedUriConfigurationService(preferences, audit);

        service.Invoking(item => item.Add(
                "https://example.com/*",
                SecurityAuditInitiator.TypedCommand))
            .Should().Throw<InvalidDataException>();
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested,
            SecurityAuditOutcome.Failed);
        service.Invoking(item => item.GetSettings()).Should().Throw<InvalidDataException>();
    }

    private sealed class Preferences : IPreapprovedUriPreferences
    {
        public PreapprovedUriSettings Saved { get; private set; } = PreapprovedUriSettings.Empty;
        public bool CorruptReadBack { get; init; }

        public PreapprovedUriSettings Load() =>
            CorruptReadBack && Saved.Patterns.Count > 0
                ? throw new InvalidDataException("Corrupt readback.")
                : Saved;

        public void Save(PreapprovedUriSettings settings) => Saved = settings;
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
