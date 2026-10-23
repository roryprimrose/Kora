namespace Kora.Setup;

public sealed record OptionalComponents(bool PowerShell = false, bool LocalInference = false, bool Kokoro = false)
{
    private readonly IReadOnlyList<SpeechComponentInstallChoice> speech = [];

    public IReadOnlyList<SpeechComponentInstallChoice> Speech
    {
        get => speech;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            speech = Array.AsReadOnly(value.ToArray());
        }
    }

    public bool Any => PowerShell || LocalInference || Kokoro || Speech.Count != 0;

    public bool Equals(OptionalComponents? other) => other is not null
        && PowerShell == other.PowerShell && LocalInference == other.LocalInference && Kokoro == other.Kokoro
        && Speech.SequenceEqual(other.Speech);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PowerShell);
        hash.Add(LocalInference);
        hash.Add(Kokoro);
        foreach (var choice in Speech) { hash.Add(choice); }
        return hash.ToHashCode();
    }
}
