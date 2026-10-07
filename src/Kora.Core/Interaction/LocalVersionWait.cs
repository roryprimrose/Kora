namespace Kora.Core.Interaction;

public static class LocalVersionWait
{
    public const string Source = "host.local-version.v1";

    public static QuestionSpec CreateSpec() => new(
        "Show the local Kora version and private-storage disclosure?",
        QuestionKind.SingleChoice, [new("show", "Show local version")],
        purpose: "local-version", sourceId: "host");

    public static bool Matches(HostQuestionRecord question) =>
        question.Proposal is null && question.Spec.Kind == QuestionKind.SingleChoice
        && string.Equals(question.Spec.Purpose, "local-version", StringComparison.Ordinal)
        && string.Equals(question.Spec.SourceId, "host", StringComparison.Ordinal)
        && question.Spec.Options.Length == 1 && string.Equals(question.Spec.Options[0].Id, "show", StringComparison.Ordinal);
}
