using System.Text.Json.Serialization;

namespace Kora.Core.Voice;

/// <summary>Presentation requested for one new task, never a saved preference.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskResponseMode>))]
public enum TaskResponseMode
{
    /// <summary>Capture the ordinary configured mode at review.</summary>
    Inherit,
    /// <summary>Show the answer without ordinary speech.</summary>
    VisualOnly,
    /// <summary>Show the answer and admit ordinary speech.</summary>
    Hybrid,
    /// <summary>Prefer speech, preserving mandatory visual output.</summary>
    VoiceOnly,
}
