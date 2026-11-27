using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Core.Agents;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Windows.Artifacts;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class HostedClaudeSessionRequestTests
{
    [Theory]
    [InlineData(CustomizationKind.Agent, "complete")]
    [InlineData(CustomizationKind.Skill, "complete")]
    [InlineData(CustomizationKind.Agent, "changed-source")]
    public async Task SelectedSourceUsesCanonicalRetentionActualSdkPostsAndExactReceipt(CustomizationKind kind, string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await WindowsSqliteConversationTests.FinishInitial(f);
        await using var c = new Composition(f);
        await c.InitializeAsync();
        await using var admission = new InstructionControlAdmission(f.Store, f.Store, new(f.Tasks));
        using var catalogue = Kora.Definitions.Artifacts.EmbeddedArtifactCatalogue.Load();
        var profileRoot = Path.Combine(f.Paths.LocalRoot, "selected-profile");
        var store = new WindowsCustomizationStore(f.Paths, profileRoot, Path.Combine(f.Paths.LocalRoot, "selected-user"));
        var source = await store.Create(kind, CustomizationScope.Kora, "reviewed", f.Token);
        var text = kind == CustomizationKind.Agent
            ? OwnedAgentProfileDocument.ProposeToolFree("reviewed", string.Empty, "Reviewed", "Use brief paragraphs.").Document.Text
            : "---\nname: reviewed\nversion: 1.0.0\ndescription: Reviewed\n---\nUse brief paragraphs.";
        await store.Save(source, null, [new(source.PrimaryFile, System.Text.Encoding.UTF8.GetBytes(text))],
            () => true, operation => Task.FromResult(operation()), f.Token);
        var management = new CustomizationManagementService(store, admission, new(f.Paths), catalogue, c.Http);
        await using var workflow = new HostedCustomizationWorkflow(management, c.Workspace, c.Conversation,
            c.Turns, c.Http, f.Time, ProviderService.Claude);
        var target = await c.Workspace.CreateAsync(new("Selected Claude target"), RequestOrigin.LocalUi, f.Token);
        const string original = "  unchanged original\r\ntask  ";

        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        var selected = await workflow.ReviewAsync(source, target.Metadata!.Name.Value, original, RequestOrigin.LocalUi,
            (_, _) => Task.FromResult(true), () => true, f.Token);
        f.Count("conversation_messages").Should().Be(0);
        c.Http.Requests.Should().OnlyContain(request => request.Method == "GET");
        await workflow.ApproveHostedSourceAsync(selected.Id, () => true, f.Token);
        f.Count("conversation_messages").Should().Be(0);
        if (fault is "changed-source")
        {
            File.WriteAllBytes(source.Location, System.Text.Encoding.UTF8.GetBytes(text + "\nChanged guidance."));
            await Assert.ThrowsAnyAsync<Exception>(() => workflow.ConfirmAsync(selected.Id, () => true, f.Token));
            c.Http.Requests.Count(request => request.Method is "POST").Should().Be(0);
        }
        else
        {
            var receipt = await workflow.ConfirmAsync(selected.Id, () => true, f.Token);
            receipt.Message.Body.Should().Be(original);
            receipt.Message.Request.SessionId.Should().Be(target.Authority.SessionId);
            receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Succeeded);
            receipt.Message.AssistantResponse.Should().NotBeNull();
            receipt.Message.AssistantResponse!.Body.Should().Be("genuine exact answer");
            foreach (var post in c.Http.Requests.Where(request => request.Method is "POST"))
            {
                using var physical = JsonDocument.Parse(post.Body);
                var messages = physical.RootElement.GetProperty("messages");
                messages[0].GetProperty("role").GetString().Should().Be("user");
                messages[0].GetProperty("content")[0].GetProperty("text").GetString().Should().Be(text);
                messages[1].GetProperty("content")[0].GetProperty("text").GetString().Should().Be(original);
            }
            f.Count("conversation_messages").Should().Be(1);
            (await f.Store.ReadConversationMessageAsync(target.Authority.SessionId, receipt.Message.Request.RequestId, f.Token))
                .Should().Be(receipt.Message);
            c.Http.Requests.Count(request => request.Method is "POST").Should().Be(2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ConfirmAsync(selected.Id, () => true, f.Token));
        }
        c.Turns.IsQuiescent.Should().BeTrue();
        c.Runtime.IsQuiescent.Should().BeTrue();
        WindowsSqliteConversationTests.AuditText(f).Should().NotContain(original).And.NotContain(text);
    }
}
