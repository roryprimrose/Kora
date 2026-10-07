namespace Kora.Core.Configuration;

public static class AssistantNameOption
{
    public const int SchemaVersion = 1;
    public const string Id = "assistant.name";
    public const string SpokenName = "assistant name";
    public const string AuditAction = "configuration.assistant-name";
    public const string Type = "string";
    public const string Scope = "device-local";
    public const string Effect = "display and activated PTT command prefix, not authority or production wake";
    public const string Availability = "active unlocked owning local host; original voice writes denied during protected/unknown calls";
    public const string Confirmation = "explicit local Apply or exact set/reset; no model authority";
    public const string ApplicationTiming = "after atomic save; capture retired, explicit listening recovery required";
    public const string ResetEffect = "restore Kora only; no identity, session, grant, approval or path reset";
    public static string Default => AssistantNameRules.DefaultName;
    public static int MaximumLength => AssistantNameRules.MaximumLength;
    public static int MaximumWordCount => AssistantNameRules.MaximumWordCount;
}
