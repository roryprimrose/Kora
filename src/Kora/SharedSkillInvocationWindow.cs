using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Kora.Controls;

namespace Kora;

internal sealed class SharedSkillInvocationWindow : Window
{
    internal TextBox Instructions { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 220, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal TextBox Request { get; } = new() { AcceptsReturn = true, IsUndoEnabled = false, Height = 100 };
    internal TextBox Address { get; } = new() { IsUndoEnabled = false };
    internal TextBox SecondAddress { get; } = new() { IsUndoEnabled = false };
    internal ComboBox McpServers { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch,
        DisplayMemberBinding = new Avalonia.Data.Binding("Display") };
    internal ComboBox McpAccounts { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch,
        DisplayMemberBinding = new Avalonia.Data.Binding("DisplayName") };
    internal TextBox McpArguments { get; } = new() { AcceptsReturn = true, IsUndoEnabled = false, Height = 100 };
    internal TextBox McpResult { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 160 };
    internal TextBox McpQuestion { get; } = new() { AcceptsReturn = true, IsUndoEnabled = false, Height = 100, IsEnabled = false };
    internal TextBox McpAnswerReview { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 200 };
    internal TextBox McpAnswerOutcome { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 100 };
    internal Button McpConfirmAnswer { get; } = new() { Content = "Confirm one fresh local-only no-tools answer", IsEnabled = false };
    internal ComboBox Targets { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBlock Status { get; } = new NamedTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    internal TextBox Confirmation { get; } = new() { IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false, Height = 150 };
    internal Button Confirm { get; } = new() { Content = "Confirm exact skill, original request and target", IsEnabled = false };
    internal Button ReadReferences { get; } = new() { Content = "Authorize only the exact declared local Markdown reads", IsVisible = false };
    internal Button Stop { get; } = new() { Content = "Stop and close reviewed use" };
    private readonly List<Button> operationButtons = [];

    internal SharedSkillInvocationWindow(Action<string> run, bool toolCapable = false, bool mcp = false)
    {
        Title = mcp ? "Reviewed read-only MCP skill" : toolCapable ? "Reviewed one-tool shared skill" : "Reviewed instruction-only shared skill";
        Width = 850;
        Height = 850;
        AutomationProperties.SetName(Instructions, "Complete inert shared skill instructions");
        AutomationProperties.SetName(Targets, "Existing Active conversation target");
        AutomationProperties.SetName(Confirmation, "Exact reviewed invocation and finite web scope");
        AutomationProperties.SetName(Status, "Reviewed skill status and recovery");
        AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = (mcp
                ? "Complete inert untrusted guidance for ONE exact host-admitted read-only MCP tool and local continuation. "
                    + "Independently choose an included server and delegated account, enter the unchanged request and exact JSON arguments. "
                    + "Source, current discovery/schema, permission and egress reviews remain separate. No scripts, other tools or hosted disclosure. "
                : toolCapable
                ? "Complete inert untrusted instructions. Local model only; one or two explicit destinations, at most one host WebPageGet call per destination. "
                    + "Source reading, model context consent and per-action permission are separate. No scripts, agents, MCP or hosted disclosure. "
                    + "Select a target, then review the exact tool revision. "
                : "Complete inert untrusted instructions. Local model only; ZERO tools, network or side effects. ")
                + "Enablement is separate, memory-only, expires in two minutes and is single-use. Closing or admission/source changes revoke it.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(Instructions);
        panel.Children.Add(ReadReferences);
        ReadReferences.Click += (_, _) => run("Prepare");
        AddButton("Enable exact reviewed revision", "Enable");
        AddButton("Refresh existing Active conversations", "Targets");
        AddButton("Next conversation page", "NextTargets");
        panel.Children.Add(Targets);
        if (mcp)
        {
            AddButton("Refresh included MCP servers and delegated accounts", "McpOptions");
            AutomationProperties.SetName(McpServers, "Independently selected included MCP server");
            AutomationProperties.SetName(McpAccounts, "Independently selected delegated MCP account");
            AutomationProperties.SetName(McpArguments, "Exact original MCP JSON arguments");
            AutomationProperties.SetName(McpResult, "Actual inert MCP result and provenance");
            panel.Children.Add(McpServers);
            panel.Children.Add(McpAccounts);
            panel.Children.Add(new TextBlock { Text = "Exact original JSON arguments (schema review and send approval remain separate)" });
            panel.Children.Add(McpArguments);
        }
        else if (toolCapable)
        {
            panel.Children.Add(new TextBlock { Text = "First explicit public page address (separately authorized per action)" });
            AutomationProperties.SetName(Address, "First explicit public page address");
            panel.Children.Add(Address);
            panel.Children.Add(new TextBlock { Text = "Second explicit public page address (optional; distinct URL for two-source use)" });
            AutomationProperties.SetName(SecondAddress, "Second explicit public page address, optional");
            panel.Children.Add(SecondAddress);
        }
        panel.Children.Add(new TextBlock { Text = "Original request (retained unchanged in the selected conversation)" });
        AutomationProperties.SetName(Request, "Unchanged original skill request");
        panel.Children.Add(Request);
        if (toolCapable) { AddButton("Review exact target and tool revision", "ReviewToolRevision"); }
        AddButton("Review exact invocation", "Prepare");
        panel.Children.Add(Confirmation);
        if (mcp) { panel.Children.Add(McpResult); }
        panel.Children.Add(Confirm);
        if (mcp)
        {
            AutomationProperties.SetName(McpQuestion, "Independent unchanged new question about the current MCP skill result");
            AutomationProperties.SetName(McpAnswerReview, "Complete result, provenance, channels and independent source-use consent");
            AutomationProperties.SetName(McpAnswerOutcome, "Actual retained MCP result answer or host terminal");
            panel.Children.Add(new TextBlock { Text = "New question about this complete current result (no rerun; original expiry unchanged)" });
            panel.Children.Add(McpQuestion);
            AddButton("Review one fresh local-only no-tools answer", "McpReviewAnswer");
            panel.Children.Add(McpAnswerReview);
            panel.Children.Add(McpConfirmAnswer);
            panel.Children.Add(McpAnswerOutcome);
            AddButton("Cancel current MCP result answer", "McpCancelAnswer");
            McpConfirmAnswer.Click += (_, _) => run("McpConfirmAnswer");
        }
        panel.Children.Add(Stop);
        panel.Children.Add(Status);
        Confirm.Click += (_, _) => run("Invoke");
        Stop.Click += (_, _) => run("Close");
        Content = new ScrollViewer { Content = panel };
        Instructions.ContextMenu = null;
        Instructions.ContextFlyout = null;
        Confirmation.ContextMenu = null;
        Confirmation.ContextFlyout = null;
        McpResult.ContextMenu = null;
        McpResult.ContextFlyout = null;
        McpAnswerReview.ContextMenu = null;
        McpAnswerReview.ContextFlyout = null;
        McpAnswerOutcome.ContextMenu = null;
        McpAnswerOutcome.ContextFlyout = null;

        void AddButton(string text, string action)
        {
            var button = new Button { Content = text };
            button.Click += (_, _) => run(action);
            operationButtons.Add(button);
            panel.Children.Add(button);
        }
    }

    internal void SetReferenceConsent(bool pending)
    {
        ReadReferences.IsVisible = pending;
        ReadReferences.IsEnabled = pending;
        foreach (var button in operationButtons) { button.IsEnabled = !pending; }
        Request.IsEnabled = !pending;
        Targets.IsEnabled = !pending;
    }

    internal void Clear()
    {
        Instructions.Text = string.Empty;
        Request.Text = string.Empty;
        Address.Text = string.Empty;
        SecondAddress.Text = string.Empty;
        McpServers.ItemsSource = null;
        McpAccounts.ItemsSource = null;
        McpArguments.Text = string.Empty;
        McpResult.Text = string.Empty;
        McpQuestion.Text = string.Empty;
        McpQuestion.IsEnabled = false;
        McpAnswerReview.Text = string.Empty;
        McpAnswerOutcome.Text = string.Empty;
        McpConfirmAnswer.IsEnabled = false;
        Confirmation.Text = string.Empty;
        Targets.ItemsSource = null;
        Status.Text = string.Empty;
        Confirm.IsEnabled = false;
        ReadReferences.IsEnabled = false;
    }
}
