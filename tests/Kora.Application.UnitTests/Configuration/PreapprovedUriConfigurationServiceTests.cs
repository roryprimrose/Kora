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

    [Fact]
    public void AddingACanonicalDuplicateDoesNotWriteAuditOrNotify()
    {
        var original = PreapprovedUriSettings.Create(["https://example.com/"]);
        var preferences = new Preferences { Saved = original };
        var audit = new Audit();
        var service = new PreapprovedUriConfigurationService(preferences, audit);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;

        var result = service.Add("HTTPS://EXAMPLE.COM:443", SecurityAuditInitiator.TypedCommand);

        result.Should().BeSameAs(original);
        service.GetSettings().Should().BeSameAs(original);
        preferences.LoadCalls.Should().Be(1);
        preferences.SaveCalls.Should().Be(0);
        audit.Events.Should().BeEmpty();
        notifications.Should().Be(0);
    }

    [Fact]
    public void RemovingAnExistingPatternPublishesReadBackBeforeNotifying()
    {
        var preferences = new Preferences
        {
            Saved = PreapprovedUriSettings.Create(["https://example.com/", "https://remaining.example/*"]),
        };
        var audit = new Audit();
        var service = new PreapprovedUriConfigurationService(preferences, audit);
        PreapprovedUriSettings? notifiedSettings = null;
        var notifications = 0;
        service.Changed += (sender, args) =>
        {
            sender.Should().BeSameAs(service);
            args.Should().BeSameAs(EventArgs.Empty);
            notifiedSettings = service.GetSettings();
            audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Succeeded);
            notifications++;
        };

        var result = service.Remove("HTTPS://EXAMPLE.COM:443", SecurityAuditInitiator.VoiceCommand);

        result.Patterns.Should().Equal("https://remaining.example/*");
        result.Should().BeSameAs(preferences.Saved);
        notifiedSettings.Should().BeSameAs(result);
        notifications.Should().Be(1);
        preferences.LoadCalls.Should().Be(2);
        preferences.SaveCalls.Should().Be(1);
        service.IsPreapproved(new Uri("https://example.com/")).Should().BeFalse();
        service.IsPreapproved(new Uri("https://remaining.example/page")).Should().BeTrue();
        AssertAuditPair(audit, SecurityAuditInitiator.VoiceCommand, SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public void RemovingAMissingPatternDoesNotWriteAuditOrNotify()
    {
        var original = PreapprovedUriSettings.Create(["https://example.com/"]);
        var preferences = new Preferences { Saved = original };
        var audit = new Audit();
        var service = new PreapprovedUriConfigurationService(preferences, audit);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;

        service.Invoking(item => item.Remove("https://missing.example/", SecurityAuditInitiator.LocalUser))
            .Should().Throw<InvalidOperationException>();

        service.GetSettings().Should().BeSameAs(original);
        preferences.LoadCalls.Should().Be(1);
        preferences.SaveCalls.Should().Be(0);
        audit.Events.Should().BeEmpty();
        notifications.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingOnlyWritesAuditsAndNotifiesWhenPatternsExist(bool populated)
    {
        var original = populated
            ? PreapprovedUriSettings.Create(["https://example.com/*"])
            : PreapprovedUriSettings.Empty;
        var preferences = new Preferences { Saved = original };
        var audit = new Audit();
        var service = new PreapprovedUriConfigurationService(preferences, audit);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;

        var result = service.Clear(SecurityAuditInitiator.LocalUser);

        result.Patterns.Should().BeEmpty();
        result.Should().BeSameAs(preferences.Saved);
        service.GetSettings().Should().BeSameAs(result);
        service.IsPreapproved(new Uri("https://example.com/page")).Should().BeFalse();
        preferences.LoadCalls.Should().Be(populated ? 2 : 1);
        preferences.SaveCalls.Should().Be(populated ? 1 : 0);
        notifications.Should().Be(populated ? 1 : 0);
        if (populated)
        {
            AssertAuditPair(audit, SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded);
        }
        else
        {
            result.Should().BeSameAs(original);
            audit.Events.Should().BeEmpty();
        }
    }

    [Fact]
    public void MismatchingReadBackAuditsFailureInvalidatesCacheAndNeverNotifies()
    {
        var original = PreapprovedUriSettings.Create(["https://original.example/"]);
        var readBack = PreapprovedUriSettings.Create(["https://read-back.example/"]);
        var preferences = new Preferences { Saved = original, ReadBackOverride = readBack };
        var audit = new Audit();
        var service = new PreapprovedUriConfigurationService(preferences, audit);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;
        service.GetSettings().Should().BeSameAs(original);

        service.Invoking(item => item.Add("https://example.com/*", SecurityAuditInitiator.TypedCommand))
            .Should().Throw<InvalidDataException>()
            .WithMessage("The saved preapproved URI patterns did not match their readback.");

        preferences.SaveCalls.Should().Be(1);
        preferences.LoadCalls.Should().Be(2);
        notifications.Should().Be(0);
        AssertAuditPair(audit, SecurityAuditInitiator.TypedCommand, SecurityAuditOutcome.Failed, "invalid-data");
        service.GetSettings().Should().BeSameAs(readBack);
        preferences.LoadCalls.Should().Be(3);
        service.IsPreapproved(new Uri("https://example.com/page")).Should().BeFalse();
        service.IsPreapproved(new Uri("https://original.example/")).Should().BeFalse();
        service.IsPreapproved(new Uri("https://read-back.example/")).Should().BeTrue();
        preferences.LoadCalls.Should().Be(3);
    }

    [Theory]
    [InlineData("io-error")]
    [InlineData("access-denied")]
    [InlineData("invalid-data")]
    public void SaveFailuresAuditTheReasonAndInvalidatePreviouslyLoadedSettings(string reasonCode)
    {
        Exception failure = reasonCode switch
        {
            "io-error" => new IOException("Write failed."),
            "access-denied" => new UnauthorizedAccessException("Write denied."),
            _ => new ArgumentException("Invalid settings."),
        };
        var original = PreapprovedUriSettings.Create(["https://original.example/"]);
        var preferences = new Preferences { Saved = original, SaveFailure = failure };
        var audit = new Audit();
        var service = new PreapprovedUriConfigurationService(preferences, audit);
        var notifications = 0;
        service.Changed += (_, _) => notifications++;
        service.GetSettings().Should().BeSameAs(original);

        service.Invoking(item => item.Add("https://example.com/*", SecurityAuditInitiator.TypedCommand))
            .Should().Throw<Exception>().Which.Should().BeSameAs(failure);

        preferences.Saved.Should().BeSameAs(original);
        preferences.LoadCalls.Should().Be(1);
        preferences.SaveCalls.Should().Be(1);
        notifications.Should().Be(0);
        AssertAuditPair(audit, SecurityAuditInitiator.TypedCommand, SecurityAuditOutcome.Failed, reasonCode);

        var recovered = PreapprovedUriSettings.Create(["https://recovered.example/*"]);
        preferences.Saved = recovered;
        service.GetSettings().Should().BeSameAs(recovered);
        preferences.LoadCalls.Should().Be(2);
        service.IsPreapproved(new Uri("https://original.example/")).Should().BeFalse();
        service.IsPreapproved(new Uri("https://example.com/page")).Should().BeFalse();
        service.IsPreapproved(new Uri("https://recovered.example/page")).Should().BeTrue();
        preferences.LoadCalls.Should().Be(2);
        audit.Events.Should().HaveCount(2);
    }

    private static void AssertAuditPair(
        Audit audit,
        SecurityAuditInitiator initiator,
        SecurityAuditOutcome outcome,
        string? reasonCode = null)
    {
        audit.Events.Should().HaveCount(2);
        var requested = audit.Events[0];
        var terminal = audit.Events[1];
        requested.Outcome.Should().Be(SecurityAuditOutcome.Requested);
        requested.ReasonCode.Should().BeNull();
        terminal.Outcome.Should().Be(outcome);
        terminal.ReasonCode.Should().Be(reasonCode);
        requested.CorrelationId.Should().NotBeEmpty();
        terminal.CorrelationId.Should().Be(requested.CorrelationId);
        audit.Events.Should().OnlyContain(item =>
            item.Category == SecurityAuditCategory.ConfigurationWrite
            && item.ActionId == "configuration.preapproved-uri"
            && item.TargetId == "preferences.device-local"
            && item.Initiator == initiator);
    }

    private sealed class Preferences : IPreapprovedUriPreferences
    {
        public PreapprovedUriSettings Saved { get; set; } = PreapprovedUriSettings.Empty;
        public bool CorruptReadBack { get; init; }
        public PreapprovedUriSettings? ReadBackOverride { get; init; }
        public Exception? SaveFailure { get; init; }
        public int LoadCalls { get; private set; }
        public int SaveCalls { get; private set; }

        public PreapprovedUriSettings Load()
        {
            LoadCalls++;
            return CorruptReadBack && Saved.Patterns.Count > 0
                ? throw new InvalidDataException("Corrupt readback.")
                : SaveCalls > 0 ? ReadBackOverride ?? Saved : Saved;
        }

        public void Save(PreapprovedUriSettings settings)
        {
            SaveCalls++;
            if (SaveFailure is not null)
            {
                throw SaveFailure;
            }
            Saved = settings;
        }
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Write(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }
}
