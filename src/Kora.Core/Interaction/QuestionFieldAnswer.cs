namespace Kora.Core.Interaction;

/// <summary>Literal lower-trust user data for exactly one declared field.</summary>
public sealed record QuestionFieldAnswer(string FieldId, string? Text = null, string? Choice = null);
