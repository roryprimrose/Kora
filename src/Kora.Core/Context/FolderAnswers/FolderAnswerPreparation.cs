namespace Kora.Core.Context.FolderAnswers;

public sealed record FolderAnswerPreparation(LocalFileAnswerOutcome Outcome, FolderAnswerReview? Review = null);
