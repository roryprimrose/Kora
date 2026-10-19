namespace Presence.Performance;

internal static class BgraPixelComparison
{
    public static Result Compare(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (expected.IsEmpty || expected.Length % 4 != 0)
        {
            throw new ArgumentException("Expected a non-empty 32-bit BGRA image.", nameof(expected));
        }
        if (actual.Length != expected.Length)
        {
            throw new ArgumentException("Expected matching BGRA image dimensions.", nameof(actual));
        }
        long referenceAlpha = 0;
        long alphaError = 0;
        long channelError = 0;
        long referenceChannels = 0;
        var maximumError = 0;
        var changedPixels = 0;
        var invalidPremultipliedPixels = 0;
        for (var offset = 0; offset < expected.Length; offset += 4)
        {
            var changed = false;
            for (var channel = 0; channel < 4; channel++)
            {
                var difference = Math.Abs(expected[offset + channel] - actual[offset + channel]);
                channelError += difference;
                referenceChannels += expected[offset + channel];
                maximumError = Math.Max(maximumError, difference);
                changed |= difference != 0;
            }
            changedPixels += changed ? 1 : 0;
            var alpha = actual[offset + 3];
            if (actual[offset] > alpha || actual[offset + 1] > alpha || actual[offset + 2] > alpha)
            {
                invalidPremultipliedPixels++;
            }
            referenceAlpha += expected[offset + 3];
            alphaError += Math.Abs(expected[offset + 3] - alpha);
        }
        return new Result(expected.Length, changedPixels, maximumError, channelError / (double)expected.Length,
            alphaError / (double)Math.Max(referenceAlpha, 1), channelError / (double)Math.Max(referenceChannels, 1),
            invalidPremultipliedPixels);
    }

    internal sealed record Result(int ComparedBytes, int ChangedPixels, int MaximumChannelError,
        double MeanChannelAbsoluteError, double AlphaRelativeError, double ChannelRelativeError,
        int InvalidPremultipliedPixels);
}
