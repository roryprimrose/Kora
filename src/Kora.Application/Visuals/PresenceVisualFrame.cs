using System.Runtime.InteropServices;

namespace Kora.Application.Visuals;

[StructLayout(LayoutKind.Auto)]
public readonly record struct PresenceVisualFrame(
    PresenceColor Color,
    double Opacity,
    double Scale);