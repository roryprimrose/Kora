using Kora.Core.Conversation;
using Kora.Core.Dependencies;

namespace Kora.Application.Hosting;

/// <summary>Volatile exact-conversation text with a live host fence, never a retained assistant record.</summary>
public sealed record ConversationAnswerPresentation(
    ConversationCancellationTarget Target, ModelAnswerUpdate Update, Func<bool> IsCurrent);
