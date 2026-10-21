using System.Diagnostics;

using AwesomeAssertions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Fact]
    public async Task PassiveMeasurementsDoNotWriteRenewActivityNotifyOrRetireReviewAndAlwaysFollowTheBuffer()
    {
        using var f = new Fixture(enabledLogger: true);
        var c = f.Composer;
        c.DraftMeasurement!.Utf8Bytes.Should().Be(0);
        await c.Create();
        var activities = 0;
        using var capture = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => activities++,
        };
        ActivitySource.AddActivityListener(capture);
        c.Body = "PRIVATE_MARKER \ud83d\ude42";
        c.Review();
        var reviewed = c.ReviewedBody;
        var writes = f.Writes;
        var events = 0;
        c.PropertyChanged += (_, _) => events++;
        for (var index = 0; index < 3; index++)
        {
            c.DraftMeasurement!.UnicodeScalars.Should().Be(16);
            c.DraftMeasurement.Utf8Bytes.Should().Be(19);
        }
        c.ReviewedBody.Should().Be(reviewed);
        c.CanSubmit.Should().BeTrue();
        events.Should().Be(0);
        f.Writes.Should().Be(writes);
        f.Messages.Should().BeEmpty();
        c.Body = new string('x', 4097);
        c.DraftMeasurement!.IsSaveShapeValid.Should().BeFalse();
        c.Body.Length.Should().Be(4097);
        c.Body = "\ud800";
        c.DraftMeasurement!.Utf8Bytes.Should().BeNull();
        c.Body.Should().Be("\ud800");
        c.Body = string.Empty;
        c.DraftMeasurement!.Utf8Bytes.Should().Be(0);
        f.Access.CanInspect = false;
        c.DraftMeasurement.Should().BeNull();
        f.Access.CanInspect = true;
        c.DraftMeasurement.Should().NotBeNull();
        c.Dispose();
        c.DraftMeasurement.Should().BeNull();
        activities.Should().Be(0, "edits and passive metrics never start activity or renew session activity");
    }

    [Fact]
    public async Task OriginalReuseAndSuccessfulSubmitNotifyCurrentBufferMeasurementNotHistoricalBodies()
    {
        using var f = new Fixture();
        var original = Original(f);
        var notifications = 0;
        f.Composer.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(f.Composer.DraftMeasurement)) { notifications++; }
        };
        using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(original.Request,
            Kora.Core.Diagnostics.HostActivityLayer.Desktop, Kora.Core.Diagnostics.HostOperation.Request);
        using var review = await ReviewOriginal(f, original);
        (await review.ConfirmAsync(false, TestContext.Current.CancellationToken)).Should().BeTrue();
        f.Composer.DraftMeasurement.Should().Be(Kora.Core.Conversation.ConversationText.Measure(original.Body));
        notifications.Should().BeGreaterThan(0);
        await f.Composer.Create();
        f.Composer.Review();
        await f.Composer.Submit();
        f.Composer.DraftMeasurement!.Utf8Bytes.Should().Be(0);
        f.Retained!.Body.Should().Be(original.Body);
    }
}
