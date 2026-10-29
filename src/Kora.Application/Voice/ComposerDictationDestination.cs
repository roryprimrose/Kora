namespace Kora.Application.Voice;

/// <summary>The existing host-owned input workflow; hypotheses are confined to session conversation input.</summary>
public enum ComposerDictationDestination
{
    /// <summary>The separate New request workflow, with final-only dictation.</summary>
    NewRequest,
    /// <summary>An exact existing conversation's unsent memory buffer.</summary>
    SessionComposer,
    /// <summary>An explicitly selected range in one canonical owned instruction body's volatile draft.</summary>
    OwnedInstructionBody,
}
