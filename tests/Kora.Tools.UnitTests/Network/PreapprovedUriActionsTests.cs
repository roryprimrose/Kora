using AwesomeAssertions;
using Kora.Core.Auditing;
using Kora.Core.Network;
using Kora.Tools.Network;

namespace Kora.Tools.UnitTests.Network;

public sealed class PreapprovedUriActionsTests
{
    [Fact]
    public void ListForwardsToConfigurationAndReturnsTheSameSettings()
    {
        var configuration = new Configuration();
        var action = new PreapprovedUriList(configuration);

        var result = action.Execute();

        result.Should().BeSameAs(configuration.Result);
        configuration.Calls.Should().Equal(nameof(IPreapprovedUriConfiguration.GetSettings));
        configuration.Pattern.Should().BeNull();
        configuration.Initiator.Should().BeNull();
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData(SecurityAuditInitiator.TypedCommand)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public void AddForwardsTheExactPatternAndInitiatorAndReturnsTheSameSettings(SecurityAuditInitiator initiator)
    {
        var configuration = new Configuration();
        var action = new PreapprovedUriAdd(configuration);
        const string pattern = "HTTPS://*.EXAMPLE.COM:443/*";

        var result = action.Execute(pattern, initiator);

        result.Should().BeSameAs(configuration.Result);
        configuration.Calls.Should().Equal(nameof(IPreapprovedUriConfiguration.Add));
        configuration.Pattern.Should().Be(pattern);
        configuration.Initiator.Should().Be(initiator);
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData(SecurityAuditInitiator.TypedCommand)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public void RemoveForwardsTheExactPatternAndInitiatorAndReturnsTheSameSettings(SecurityAuditInitiator initiator)
    {
        var configuration = new Configuration();
        var action = new PreapprovedUriRemove(configuration);
        const string pattern = "HTTPS://EXAMPLE.COM:443/";

        var result = action.Execute(pattern, initiator);

        result.Should().BeSameAs(configuration.Result);
        configuration.Calls.Should().Equal(nameof(IPreapprovedUriConfiguration.Remove));
        configuration.Pattern.Should().Be(pattern);
        configuration.Initiator.Should().Be(initiator);
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.LocalUser)]
    [InlineData(SecurityAuditInitiator.TypedCommand)]
    [InlineData(SecurityAuditInitiator.VoiceCommand)]
    public void ClearForwardsTheExactInitiatorAndReturnsTheSameSettings(SecurityAuditInitiator initiator)
    {
        var configuration = new Configuration();
        var action = new PreapprovedUriClear(configuration);

        var result = action.Execute(initiator);

        result.Should().BeSameAs(configuration.Result);
        configuration.Calls.Should().Equal(nameof(IPreapprovedUriConfiguration.Clear));
        configuration.Pattern.Should().BeNull();
        configuration.Initiator.Should().Be(initiator);
    }

    private sealed class Configuration : IPreapprovedUriConfiguration
    {
        public PreapprovedUriSettings Result { get; } = PreapprovedUriSettings.Create(["https://result.example/"]);
        public List<string> Calls { get; } = [];
        public string? Pattern { get; private set; }
        public SecurityAuditInitiator? Initiator { get; private set; }

        public PreapprovedUriSettings GetSettings() => Record(nameof(GetSettings));

        public PreapprovedUriSettings Add(string pattern, SecurityAuditInitiator initiator) =>
            Record(nameof(Add), pattern, initiator);

        public PreapprovedUriSettings Remove(string pattern, SecurityAuditInitiator initiator) =>
            Record(nameof(Remove), pattern, initiator);

        public PreapprovedUriSettings Clear(SecurityAuditInitiator initiator) =>
            Record(nameof(Clear), initiator: initiator);

        public bool IsPreapproved(Uri uri) => throw new NotSupportedException();

        private PreapprovedUriSettings Record(
            string operation,
            string? pattern = null,
            SecurityAuditInitiator? initiator = null)
        {
            Calls.Add(operation);
            Pattern = pattern;
            Initiator = initiator;
            return Result;
        }
    }
}
