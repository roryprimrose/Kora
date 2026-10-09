namespace Kora.Core.Voice;

public static class ResponseOutputModeResolver
{
    public static ResponseOutputMode Resolve(
        ResponseOutputMode defaultMode,
        ResponseOutputMode? queueOverride,
        ResponseOutputMode? taskOverride,
        ResponseOutputMode? sessionOverride = null)
    {
        Validate(defaultMode, nameof(defaultMode));
        Validate(queueOverride, nameof(queueOverride));
        Validate(taskOverride, nameof(taskOverride));
        Validate(sessionOverride, nameof(sessionOverride));

        return taskOverride ?? queueOverride ?? sessionOverride ?? defaultMode;
    }

    private static void Validate(ResponseOutputMode? mode, string parameterName)
    {
        if (mode is not null && !Enum.IsDefined(mode.Value))
        {
            throw new ArgumentOutOfRangeException(parameterName, mode, "The response output mode is invalid.");
        }
    }
}