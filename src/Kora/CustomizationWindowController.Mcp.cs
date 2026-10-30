using System.Text;
using System.Text.Json;

using Kora.Core.Customizations;

namespace Kora;

internal sealed partial class CustomizationWindowController
{
    private static readonly JsonSerializerOptions McpEntryJson = new() { WriteIndented = true };
    private async Task<bool> ConsentMcpRead(CustomizationSource selected, Func<bool> current, CancellationToken token)
    {
        if (selected.Scope != CustomizationScope.Kora)
        {
            window!.Status.Text = "Personal and additional MCP files remain metadata-only here. No endpoint, launch configuration or credentials were disclosed. Add one explicit Kora HTTPS entry instead.";
            return false;
        }
        var choice = await window!.Review("Review private MCP source read",
            selected.Name + "\n" + selected.Id + "\n" + selected.Location
            + "\nRead only this exact selected entry into native HTTPS fields. Other entries stay undisclosed and unchanged."
            + "\nOnly url and optional type http are editable. Additional root fields and unsupported or sensitive selected-entry fields fail closed."
            + "\nNo model disclosure, source inclusion, connection, account, discovery, tool or grant is approved.",
            token, "Read selected HTTPS entry", "Cancel");
        RequireCurrent(current, token);
        if (choice is not "Read selected HTTPS entry")
        { window.Status.Text = "MCP source read cancelled. No endpoint was disclosed and nothing was written."; }
        return choice is "Read selected HTTPS entry";
    }

    private void ShowMcpFields()
    {
        if (window is null || source?.Kind != CustomizationKind.McpServer) { return; }
        var file = draft.SingleOrDefault();
        if (file is null) { window.McpUrl.Text = string.Empty; window.McpHttpType.IsChecked = false; return; }
        using var document = JsonDocument.Parse(file.Text, new JsonDocumentOptions
        { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 16 });
        window.McpUrl.Text = document.RootElement.GetProperty("url").GetString();
        window.McpHttpType.IsChecked = document.RootElement.TryGetProperty("type", out _);
    }

    private void FlushMcpFields()
    {
        if (draft.Count != 1) { return; }
        using var document = JsonDocument.Parse(draft[0].Text, new JsonDocumentOptions
        { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 16 });
        var url = window!.McpUrl.Text ?? string.Empty;
        var explicitType = window.McpHttpType.IsChecked == true;
        if (string.Equals(url, document.RootElement.GetProperty("url").GetString(), StringComparison.Ordinal)
            && explicitType == document.RootElement.TryGetProperty("type", out _)) { return; }
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["url"] = url };
        if (explicitType) { values.Add("type", "http"); }
        draft = [new(source!.PrimaryFile, JsonSerializer.SerializeToUtf8Bytes(values, McpEntryJson))];
    }

    private string McpReviewText()
    {
        var result = new StringBuilder("ONE KORA-OWNED HTTPS MCP ENTRY\nName: " + source!.Name
            + "\nSource-qualified ID: " + source.Id + "\nSource and destination: " + source.Location
            + "\nNew files use a servers object. Existing servers or mcpServers JSON/JSONC retain all bytes outside the selected entry."
            + "\nSaving first disables this identity and independently removes MCP inclusion. It does not register or include a removed customization root."
            + "\nNo authentication, account, grant, discovery, tool, model or network operation is authorized."
            + "\nThe complete file identity, bytes, stability, security and parent binding are checked immediately before same-file native atomic publication."
            + "\nThis is optimistic comparison, NOT content compare-and-swap. A last-instant same-name external edit can race replacement."
            + "\nReadback must match the published identity and exact complete file. Uncertainty stays excluded and requires fresh inspection; no rollback is claimed.\n\n");
        var before = baseline?.Files.Single().Text;
        var after = draft.Single().Text;
        result.AppendLine("COMPLETE ORIGINAL ENTRY\n" + (before ?? "(Entry absent; unrelated entries remain unchanged)"));
        result.AppendLine("COMPLETE PROPOSED ENTRY\n" + after);
        result.AppendLine("EXACT ENTRY REPLACEMENT DIFF (all original lines removed; all proposed lines added)");
        if (before is not null) { result.AppendLine("-" + before.Replace("\n", "\n-", StringComparison.Ordinal)); }
        result.AppendLine("+" + after.Replace("\n", "\n+", StringComparison.Ordinal));
        return result.ToString();
    }

    private async Task<bool> ConfirmMcpSave(string review, Func<bool> current, CancellationToken token)
    {
        if (await window!.Review("Review exact HTTPS MCP entry changes", review, token,
            "Continue to separate Save confirmation", "Cancel") is not "Continue to separate Save confirmation") { return false; }
        RequireCurrent(current, token);
        return await window.Review("Confirm atomic MCP save",
            review + "\n\nConfirm this exact single-entry publication and readback. The server stays disabled and independently excluded."
            + "\nCancel makes no configuration or use-control writes. No source-root registration, account or runtime activation is included.",
            token, "Save disabled and excluded", "Cancel") is "Save disabled and excluded" && current();
    }
}
