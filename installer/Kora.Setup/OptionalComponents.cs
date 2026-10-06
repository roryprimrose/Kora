namespace Kora.Setup;

public sealed record OptionalComponents(bool PowerShell = false, bool LocalInference = false, bool Kokoro = false)
{
    public bool Any => PowerShell || LocalInference || Kokoro;
}
