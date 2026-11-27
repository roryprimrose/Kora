namespace Kora.Application.Dependencies;

/// <summary>The provider shape an ordinary conversation request takes under the current Settings.</summary>
public enum ConversationProviderRoute
{
    Local,
    LocalFirst,
    Copilot,
    Claude,
}
