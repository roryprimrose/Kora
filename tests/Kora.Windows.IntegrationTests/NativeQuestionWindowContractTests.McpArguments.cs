using System.Text.Json;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Interaction;
using Kora.Core.Mcp;
using Kora.Windows.IntegrationTests.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class NativeQuestionWindowContractTests
{
    private static readonly string[] McpArgumentActions = ["ReviewQuestion", "SubmitAnswer", "CancelQuestion"];

    [Theory]
    [InlineData("submit")]
    [InlineData("cancel")]
    [InlineData("privacy")]
    [InlineData("expiry")]
    [InlineData("close")]
    public async Task McpArgumentsKeepCompleteSchemaKeyboardReadableAndInputSeparateFromApproval(string completion)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var fixture = new InteractionStorageFixture();
            fixture.Time.UseFrozenTimers = true;
            await fixture.InitializeAsync();
            var properties = Enumerable.Range(0, McpToolSchema.MaximumProperties).ToDictionary(
                index => "field" + index + new string('x', 58), _ => new { type = "string", maxLength = 512 },
                StringComparer.Ordinal);
            var schema = new McpToolSchema(JsonSerializer.Serialize(new
            {
                type = "object", additionalProperties = false, properties, required = properties.Keys.ToArray(),
            }));
            var arguments = JsonSerializer.Serialize(properties.ToDictionary(pair => pair.Key, _ => "reference", StringComparer.Ordinal))
                .Insert(1, new string('\n', 48));
            schema.ValidateArguments(arguments);
            var prompt = "Enter JSON arguments for mcp.search. Every required field must match this schema. "
                + "Submitting input is NOT approval to send it.\n" + schema.Json;
            var spec = new QuestionSpec(prompt, QuestionKind.Text, [],
                maximumTextLength: McpToolSchema.MaximumArgumentsUtf8Bytes, sourceId: "mcp-tool-arguments");
            var record = (await fixture.RunAsync(() => fixture.Questions.CreateAsync(
                fixture.Request, spec, fixture.Time.Now.AddMinutes(2), fixture.Token))).Question!;
            var eligible = true;
            var store = new NativeInteractionStore(fixture.Store, () => eligible);
            var model = new NativeQuestionViewModel(record, new(store, fixture.Time), new(store, fixture.Time),
                new(store, fixture.Time), () => eligible, fixture.Time, NullLogger<NativeQuestionViewModel>.Instance);
            var window = CreateWindow(model);
            try
            {
                await ShowAsync(window, model);
                var reader = window.FindControl<TextBox>("ExactReview")!;
                var input = window.FindControl<TextBox>("AnswerInput")!;
                reader.Text.Should().Be(prompt, "the entire selected schema must be readable before entering arguments");
                model.ReviewText.Should().BeEmpty("displaying a schema is not an explicit record review");
                reader.Focus().Should().BeTrue();
                window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                window.KeyRelease(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                window.UpdateLayout();
                reader.CaretIndex.Should().Be(prompt.Length);
                reader.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset.Y.Should().BeGreaterThan(0);
                reader.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "not schema" });
                reader.Text.Should().Be(prompt);
                RoutedEvent[] privateEvents = [TextBox.CopyingToClipboardEvent, TextBox.CuttingToClipboardEvent,
                    TextBox.PastingFromClipboardEvent];
                foreach (var routedEvent in privateEvents)
                {
                    var attempted = new RoutedEventArgs(routedEvent);
                    reader.RaiseEvent(attempted);
                    attempted.Handled.Should().BeTrue();
                }
                var context = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent };
                reader.RaiseEvent(context);
                context.Handled.Should().BeTrue();
                input.Focus().Should().BeTrue();
                input.Text = arguments;
                window.UpdateLayout();
                input.Bounds.Height.Should().BeInRange(1, 120);
                reader.Bounds.Height.Should().BeGreaterThan(40);
                foreach (var name in McpArgumentActions)
                {
                    var button = window.FindControl<Button>(name)!;
                    var position = button.TranslatePoint(default, window)!.Value;
                    position.Y.Should().BeGreaterThanOrEqualTo(0);
                    (position.Y + button.Bounds.Height).Should().BeLessThanOrEqualTo(window.Bounds.Height);
                    button.Focus().Should().BeTrue();
                }
                var pending = (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single();
                pending.Status.Should().Be(QuestionStatus.Pending);
                pending.Draft.Should().BeNull("schema reading and local edits must not submit or persist arguments");
                model.Completion.IsCompleted.Should().BeFalse();
                fixture.Count("scoped_grants").Should().Be(0);
                await model.ReviewAsync();
                model.ReviewText.Should().NotBeEmpty();
                reader.Text.Should().Be(model.ReviewText);

                if (completion is "privacy") { eligible = false; model.RefreshEligibility(); }
                else if (completion is "expiry") { fixture.Time.Now = record.ExpiresAt; model.RefreshEligibility(); }
                else if (completion is "close") { window.Close(); }
                else
                {
                    var button = window.FindControl<Button>(completion is "submit" ? "SubmitAnswer" : "CancelQuestion")!;
                    button.Focus().Should().BeTrue();
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                    await model.Completion.WaitAsync(fixture.Token);
                }
                var stored = (await fixture.Store.ReadQuestionsAsync(fixture.Request.SessionId, fixture.Token)).Single();
                stored.Proposal.Should().BeNull("argument entry can never create an execution approval");
                if (completion is "submit")
                {
                    stored.Status.Should().Be(QuestionStatus.Answered);
                    stored.Draft!.Text.Should().Be(arguments);
                    schema.ValidateArguments(stored.Draft.Text!);
                }
                else
                {
                    stored.Status.Should().NotBe(QuestionStatus.Answered);
                    stored.Draft.Should().BeNull();
                    await model.SubmitAsync();
                    model.CanSubmit.Should().BeFalse();
                }
                if (completion is "privacy" or "expiry")
                {
                    reader.Text.Should().BeNullOrEmpty();
                    input.Text.Should().BeNullOrEmpty();
                }
                fixture.Count("scoped_grants").Should().Be(0);
            }
            finally { window.Close(); }
            window.FindControl<TextBox>("ExactReview")!.Text.Should().BeNullOrEmpty();
            window.FindControl<TextBox>("AnswerInput")!.Text.Should().BeNullOrEmpty();
        });
    }
}
