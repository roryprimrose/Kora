using Avalonia;

namespace Kora;

internal sealed record DetailReadingPosition(bool Source, bool Continuous, Vector RenderedOffset, Vector TextOffset, int Caret);
