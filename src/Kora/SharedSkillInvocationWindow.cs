using Avalonia.Controls;

namespace Kora;

internal sealed class SharedSkillInvocationWindow : Window
{
    internal SharedSkillInvocationView View { get; }
    internal TextBox Instructions => View.Instructions;
    internal TextBox Request => View.Request;
    internal TextBox Address => View.Address;
    internal TextBox SecondAddress => View.SecondAddress;
    internal ComboBox McpServers => View.McpServers;
    internal ComboBox McpAccounts => View.McpAccounts;
    internal TextBox McpArguments => View.McpArguments;
    internal TextBox McpResult => View.McpResult;
    internal TextBox McpQuestion => View.McpQuestion;
    internal TextBox McpAnswerReview => View.McpAnswerReview;
    internal TextBox McpAnswerOutcome => View.McpAnswerOutcome;
    internal Button McpConfirmAnswer => View.McpConfirmAnswer;
    internal ComboBox Targets => View.Targets;
    internal TextBlock Status => View.Status;
    internal TextBox Confirmation => View.Confirmation;
    internal Button Confirm => View.Confirm;
    internal Button ReadReferences => View.ReadReferences;
    internal Button Stop => View.Stop;
    internal SharedSkillInvocationWindow(Action<string> run, bool toolCapable = false, bool mcp = false, bool owned = false)
    {
        Title = mcp ? "Reviewed read-only MCP skill" : toolCapable ? "Reviewed one-tool shared skill" : "Reviewed instruction-only shared skill";
        Width = 850;
        Height = 850;
        View = new(this, run, static () => true, toolCapable, mcp, owned);
        Content = View;
        Closed += (_, _) => View.Dispose();
    }
    internal void SetReferenceConsent(bool pending) => View.SetReferenceConsent(pending);
    internal void Clear() => View.Clear();
}
