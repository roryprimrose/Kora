using AwesomeAssertions;
using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class AssistantNameOptionTests
{
    [Fact]
    public void Descriptor_reuses_authoritative_legacy_name_semantics()
    {
        AssistantNameOption.SchemaVersion.Should().Be(1);
        AssistantNameOption.Id.Should().Be("assistant.name");
        AssistantNameOption.SpokenName.Should().Be("assistant name");
        AssistantNameOption.Type.Should().Be("string");
        AssistantNameOption.Default.Should().Be(AssistantNameRules.DefaultName);
        AssistantNameOption.MaximumLength.Should().Be(AssistantNameRules.MaximumLength);
        AssistantNameOption.MaximumWordCount.Should().Be(AssistantNameRules.MaximumWordCount);
        AssistantNameOption.Scope.Should().Be("device-local");
        AssistantNameOption.Effect.Should().Contain("not authority or production wake");
        AssistantNameOption.Availability.Should().Contain("owning local host");
        AssistantNameOption.Confirmation.Should().Contain("exact set/reset");
        AssistantNameOption.ApplicationTiming.Should().Contain("capture retired");
        AssistantNameOption.ResetEffect.Should().Contain("no identity");
        AssistantNameOption.AuditAction.Should().Be("configuration.assistant-name");
    }
}
