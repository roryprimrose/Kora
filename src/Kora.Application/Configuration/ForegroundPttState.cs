using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record ForegroundPttState(ForegroundPttShortcut Shortcut, long Revision);
