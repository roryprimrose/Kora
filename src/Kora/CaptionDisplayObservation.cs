using Avalonia;

namespace Kora;

internal sealed record CaptionDisplayObservation(CaptionDisplayLifetime Lifetime, PixelRect WorkingArea,
    double Scaling, bool IsPrimary);
