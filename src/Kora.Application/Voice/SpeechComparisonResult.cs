using System.Globalization;

using Kora.Core.Voice;

namespace Kora.Application.Voice;

public sealed record SpeechComparisonResult(string Profile, int Trial, string Expected, SpeechComparisonObservation Observation)
{
    public bool Correct => string.Equals(Expected.Trim(), Observation.FinalText.Trim(), StringComparison.OrdinalIgnoreCase);
    public string Description => string.Create(CultureInfo.InvariantCulture,
        $"{Profile}, {(Trial == 1 ? "first" : "repeat")}: {(Correct ? "correct" : "mismatch")} (case-insensitive exact text). Expected: {Expected}. Final: {Observation.FinalText}. "
        + $"Measured open-to-start: {Observation.OpenToStart.TotalMilliseconds:F1} ms. End-of-speech-to-final: unknown. Cold startup: unknown. Warm startup: unknown. Resource use: unknown.");
}
