using System.Text;
using AwesomeAssertions;
using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteSessionAttachmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachmentAnswerEvidenceReviewReadsExactDatabaseCopyWithoutOriginalPathOrRetentionRenewal(bool keep)
    {
        using var fixture = await Initialize();
        var source = await Attach(fixture, Encoding.UTF8.GetBytes("alpha immutable database source"));
        if (keep)
        {
            await NewControl(fixture, source.Session);
            await fixture.RunAsync(async () =>
            {
                await fixture.Store.SetPerpetualAsync(fixture.Request, new(1), true, static () => true, fixture.Token);
                await fixture.Tasks.CommitAsync(new(fixture.Request, new(2), HostTaskState.Succeeded), 1, fixture.Token);
            });
        }
        var clock = await fixture.Store.ReadRetentionAsync(source.Session, fixture.Token);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        var access = new WindowsSqliteSessionWorkspaceTests.Access();
        var workspace = WindowsSqliteSessionWorkspaceTests.Service(fixture, access);
        var workflow = new SessionAttachmentAnswerWorkflow(workspace, access, new LocalFileLexicalRetrieval(),
            new UnavailableLocalFileModelUsePolicy(), fixture.Time, new Audit(), NullLogger<SessionAttachmentAnswerWorkflow>.Instance);
        var target = await fixture.Store.ReadMetadataAsync(source.Session, fixture.Token);
        using var review = await workflow.Prepare(target, source, "alpha", static () => true, fixture.Token);
        review.Evidence.Citations.Single().Source.Should().Be(source.File.Reference);
        review.Evidence.Citations.Single().Excerpt.Should().Be(source.File.Text);
        (await fixture.Store.ReadRetentionAsync(source.Session, fixture.Token)).Should().Be(clock);
        fixture.Count("conversation_messages").Should().Be(0);
        var submitted = 0;
        await workflow.Invoking(value => value.Submit(review, (_, _, _, _, _) =>
        {
            submitted++;
            throw new InvalidOperationException("Source-use policy is unavailable");
        }, Request(source.Session), static () => true, fixture.Token))
            .Should().ThrowAsync<SessionAttachmentAnswerUnavailableException>();
        submitted.Should().Be(0);
        fixture.Count("conversation_messages").Should().Be(0);
        (await fixture.Store.ReadRetentionAsync(source.Session, fixture.Token)).Should().Be(clock);
    }
}
