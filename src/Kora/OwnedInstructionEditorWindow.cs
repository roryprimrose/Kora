using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Kora.Application.Definitions;
using Kora.Controls;
using Kora.Core.Artifacts;

namespace Kora;

internal sealed class OwnedInstructionEditorWindow : Window
{
    internal OwnedInstructionEditorView View { get; }
    internal ComboBox Files => View.Files;
    internal TextBox QueryInput => View.QueryInput;
    internal ComboBox QueryModeInput => View.QueryModeInput;
    internal ComboBox OrderInput => View.OrderInput;
    internal ComboBox UseStateInput => View.UseStateInput;
    internal ComboBox KindInput => View.KindInput;
    internal Button ReviewUse => View.ReviewUse;
    internal Button DisableReviewedUse => View.DisableReviewedUse;
    internal Button RestorePrevious => View.RestorePrevious;
    internal Button NextPage => View.NextPage;
    internal TextBox NameInput => View.NameInput;
    internal TextBox CopyNameInput => View.CopyNameInput;
    internal TextBox RenameNameInput => View.RenameNameInput;
    internal TextBox Critique => View.Critique;
    internal TextBox Source => View.Source;
    internal TextBlock SourceSizeStatus => View.SourceSizeStatus;
    internal TextBox BufferFindQuery => View.BufferFindQuery;
    internal Button BufferFindFirst => View.BufferFindFirst;
    internal Button BufferFindNext => View.BufferFindNext;
    internal Button BufferFindPrevious => View.BufferFindPrevious;
    internal Button BufferFindClear => View.BufferFindClear;
    internal NamedTextBlock BufferFindOutcome => View.BufferFindOutcome;
    internal Button GoToLineButton => View.GoToLineButton;
    internal TextBox LineInput => View.LineInput;
    internal Button LineGo => View.LineGo;
    internal Button LineCancel => View.LineCancel;
    internal TextBox MetadataFindQuery => View.MetadataFindQuery;
    internal Button MetadataFindFocus => View.MetadataFindFocus;
    internal Button MetadataFindFirst => View.MetadataFindFirst;
    internal Button MetadataFindNext => View.MetadataFindNext;
    internal Button MetadataFindPrevious => View.MetadataFindPrevious;
    internal Button MetadataFindClear => View.MetadataFindClear;
    internal NamedTextBlock MetadataFindMatch => View.MetadataFindMatch;
    internal NamedTextBlock MetadataFindStatus => View.MetadataFindStatus;
    internal OwnedInstructionBodyDictation? BodyDictation => View.BodyDictation;

    internal OwnedInstructionEditorWindow(Action<string> run)
    {
        Title = "Kora instruction and prompt editor";
        Width = 960;
        Height = 720;
        View = new(this, run, static () => true);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Closed += (_, _) => View.Dispose();
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!ReferenceEquals(args.Source, this)) { return; }
            var forwarded = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = args.Key, KeyModifiers = args.KeyModifiers };
            View.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }, RoutingStrategies.Tunnel);
    }

    internal void Report(string text) => View.Report(text);
    internal void SetBodyDictation(OwnedInstructionBodyDictation owner) => View.SetBodyDictation(owner);
    internal void ClearSearch() => View.ClearSearch();
    internal void ReportValidation(string text) => View.ReportValidation(text);
    internal void ClearValidation() => View.ClearValidation();
    internal void ReportInventory(string text) => View.ReportInventory(text);
    internal void ShowInventory(OwnedDefinitionInventoryPage page) => View.ShowInventory(page);
    internal void ClearInventory() => View.ClearInventory();
    internal void SelectKind(ArtifactKind kind) => View.SelectKind(kind);
    internal void CancelReview() => View.CancelReview();
    internal void SetUseReviewAvailable(bool available) => View.SetUseReviewAvailable(available);
    internal void SetRestoreAvailable(bool available) => View.SetRestoreAvailable(available);
    internal void ClearPrivateText() => View.ClearPrivateText();
    internal void ShowComparison(OwnedDraftComparison comparison, Func<bool> current) => View.ShowComparison(comparison, current);
    internal Task<int> Choose(string title, string details, string? original, string? proposed, string[] choices, CancellationToken token,
        Func<Window, CancellationToken, Task<bool>>? spokenSave = null) =>
        View.Choose(title, details, original, proposed, choices, token, spokenSave);
    internal void BindBufferFind(Func<Func<bool>?> capture) => View.BindBufferFind(capture);
    internal void RetireBufferFind(bool clearQuery = false) => View.RetireBufferFind(clearQuery);
    internal void BindGoToLine(Func<Func<bool>?> capture) => View.BindGoToLine(capture);
    internal void InvalidateGoToLine() => View.InvalidateGoToLine();
    internal void AdmitMetadataFind(OwnedDefinitionInventoryRow row, Func<bool> current) => View.AdmitMetadataFind(row, current);
    internal void RetireMetadataFind() => View.RetireMetadataFind();
    internal void CancelRebaseReview() => View.CancelRebaseReview();
    internal Task<bool> ReviewRebase(OwnedDraftRebase proposal, Func<bool> current, CancellationToken token) =>
        View.ReviewRebase(proposal, current, token);
}
