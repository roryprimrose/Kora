using Avalonia;

namespace Kora;

internal sealed record DetailReadingPosition(bool Source, bool Continuous, bool WrapText, Vector RenderedOffset, Vector TextOffset, int Caret);
