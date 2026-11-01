namespace Kora.Core.Configuration;

/// <summary>A confirmed presentation preference, not launch, input or private-source authority.</summary>
public enum StartupPresentation
{
    /// <summary>Keep the existing presence/tray initialization without opening the workspace.</summary>
    Tray,
    /// <summary>Show the existing workspace without selecting or reading a conversation.</summary>
    Workspace,
}
