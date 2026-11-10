using Avalonia.Controls;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Context;
using Kora.Core.Context.FolderAnswers;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed class LocalFilePreviewWindow : Window
{
    internal LocalFilePreviewView View { get; }
    internal LocalFilePreviewWindow(Action<string> reportFailure, MainViewModel? typographyHost = null)
    {
        View = new(this, reportFailure, typographyHost);
        Content = View;
    }

    internal void ShowReview(LocalFileReview review, Func<Task> confirm) => View.ShowReview(review, confirm);
    internal void ShowFolderReview(LocalFolderReview review, Func<Task> confirm) => View.ShowFolderReview(review, confirm);
    internal void ShowFolderRevision(LocalFolderRevision revision,
        Func<string, LocalFileRetrievalMode, CancellationToken, Task<LocalFileSearchResult>> search, Func<bool> isCurrent,
        Func<Task> refresh,
        Func<string, Func<bool>, CancellationToken, Task<FolderAnswerPreparation>>? prepareAnswer = null,
        Func<FolderAnswerReview, Func<bool>, CancellationToken, Task<LocalFileAnswerResult>>? answerFolder = null) =>
        View.ShowFolderRevision(revision, search, isCurrent, refresh, prepareAnswer, answerFolder);
    internal void ShowRevision(LocalFileRevision revision,
        Func<string, LocalFileRetrievalMode, CancellationToken, Task<LocalFileSearchResult>> search, Func<bool> isCurrent,
        Func<Task> refresh,
        Func<string, LocalContextSessionTarget, Func<bool>, CancellationToken, Task<LocalFileAnswerResult>>? answer = null) =>
        View.ShowRevision(revision, search, isCurrent, refresh, answer);
    internal void FocusSearch() => View.FocusSearch();
    internal void FocusAnswer() => View.FocusAnswer();
    internal bool PrepareSearch(string query) => View.PrepareSearch(query);
    internal void ShowAttachmentReview(LocalFileReview review, HostRevision generation, Func<Task> confirm) =>
        View.ShowAttachmentReview(review, generation, confirm);
    internal void ShowAttachment(SessionFileAttachment attachment,
        Func<string, LocalFileRetrievalMode, CancellationToken, Task<LocalFileSearchResult>> search,
        Func<bool> isCurrent, Func<Task>? remove, Func<Task>? replace = null,
        Func<CancellationToken, Task<LocalFileRevision>>? resolveCurrent = null) =>
        View.ShowAttachment(attachment, search, isCurrent, remove, replace, resolveCurrent);
    internal void ShowAttachmentReplacement(SessionFileReplacement review, Func<Task> confirm) =>
        View.ShowAttachmentReplacement(review, confirm);
    internal void ShowAttachmentRemoval(SessionFileRemoval review, Func<Task> confirm) => View.ShowAttachmentRemoval(review, confirm);
    internal void ShowAttachmentAnswer(SessionFileAttachment source,
        Func<string, Func<bool>, CancellationToken, Task<SessionAttachmentAnswerReview>> prepare,
        Func<SessionAttachmentAnswerReview, Func<bool>, CancellationToken, Task<ConversationSubmissionReceipt>> submit,
        Func<bool> current, CancellationToken token) => View.ShowAttachmentAnswer(source, prepare, submit, current, token);
    internal void ClearAndClose() => View.ClearAndClose();
}
