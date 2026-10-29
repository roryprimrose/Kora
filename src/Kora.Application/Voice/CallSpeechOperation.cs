namespace Kora.Application.Voice;

/// <summary>Separate original-user operations on one exact call speech review.</summary>
internal enum CallSpeechOperation
{
    Review,
    Confirm,
    Cancel,
    Status,
}
