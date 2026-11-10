using Avalonia.Controls;

using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora;

internal sealed class OwnedSkillEditorWindow : Window, IOwnedSkillSaveReview, IOwnedSkillRenameReview, IOwnedSkillDeletionReview
{
    internal OwnedSkillEditorView View { get; }
    internal TextBox IdInput => View.IdInput;
    internal TextBox VersionInput => View.VersionInput;
    internal TextBox NameInput => View.NameInput;
    internal TextBox DescriptionInput => View.DescriptionInput;
    internal TextBox Guidance => View.Guidance;
    internal TextBox Critique => View.Critique;
    internal TextBox Fixture => View.Fixture;
    internal TextBox Purpose => View.Purpose;
    internal TextBox ExpectedInput => View.ExpectedInput;
    internal TextBox ExpectedOutput => View.ExpectedOutput;
    internal ComboBox Saved => View.Saved;
    internal TextBox RenameId => View.RenameId;
    internal ComboBox PendingRenames => View.PendingRenames;
    internal ComboBox Tool => View.Tool;
    internal TextBox McpServer => View.McpServer;
    internal TextBox McpSourceRevision => View.McpSourceRevision;
    internal TextBox McpTool => View.McpTool;
    internal TextBox McpSchemaDigest => View.McpSchemaDigest;
    internal ComboBox InputFormat => View.InputFormat;
    internal TextBox ComponentPath => View.ComponentPath;
    internal TextBox ComponentBody => View.ComponentBody;
    internal OwnedDeclarativeSkillSnapshot? BasePackage { get => View.BasePackage; set => View.BasePackage = value; }

    internal OwnedSkillEditorWindow(Action<string> run, Func<bool> eligible)
    {
        Title = "New Kora-owned declarative skill";
        Width = 900;
        Height = 700;
        View = new(this, run, eligible, static () => true);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Closed += (_, _) => View.Dispose();
    }

    internal OwnedDeclarativeSkillDocument Document() => View.Document();
    internal void Report(string message) => View.Report(message);
    internal bool ShowForPrivateRecovery(Func<bool> privateEligible) => View.ShowForPrivateRecovery(privateEligible);
    internal void RetireReview() => View.RetireReview();
    internal void ApplyComponent(string path, string body) => View.ApplyComponent(path, body);
    internal void RemoveComponent(string path) => View.RemoveComponent(path);
    internal void LoadComponents(OwnedDeclarativeSkillSnapshot? snapshot) => View.LoadComponents(snapshot);
    public Task<bool> Review(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        View.Review(document, destination, token);
    public Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        View.ConfirmPublication(document, destination, token);
    public Task<bool> ReviewRename(OwnedSkillRename proposal, string source, string destination, bool recovery, CancellationToken token) =>
        View.ReviewRename(proposal, source, destination, recovery, token);
    public Task<bool> ConfirmRename(OwnedSkillRename proposal, string source, string destination, bool recovery, CancellationToken token) =>
        View.ConfirmRename(proposal, source, destination, recovery, token);
    public Task<bool> ReviewDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token) => View.ReviewDeletion(source, token);
    public Task<bool> ConfirmDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token) => View.ConfirmDeletion(source, token);
    internal Task<bool> ConfirmComponentRead(OwnedDeclarativeSkillSnapshot declaration, CancellationToken token) =>
        View.ConfirmComponentRead(declaration, token);
    internal Task<int> DirtyChoice(bool canSave, Func<bool> privateEligible, CancellationToken token) =>
        View.DirtyChoice(canSave, privateEligible, token);
    internal Task<int> ChooseGuidance(string title, string disclosure, string current, string proposed,
        string[] choices, Func<bool> currentInput, CancellationToken token) =>
        View.ChooseGuidance(title, disclosure, current, proposed, choices, currentInput, token);
    internal Task<bool> ReviewFixture(OwnedSkillFixtureWorkflow.Preview preview, Func<bool> current, CancellationToken token) =>
        View.ReviewFixture(preview, current, token);
    internal Task ShowFixtureResult(OwnedSkillFixtureWorkflow.Result result, Func<bool> current, CancellationToken token) =>
        View.ShowFixtureResult(result, current, token);
    internal Task<int> ChooseInitial(string title, string disclosure, string manifest, string guidance,
        string[] choices, Func<bool> currentInput, CancellationToken token) =>
        View.ChooseInitial(title, disclosure, manifest, guidance, choices, currentInput, token);
}
