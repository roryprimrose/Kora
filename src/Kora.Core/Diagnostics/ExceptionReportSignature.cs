using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Kora.Core.Diagnostics;

public sealed record ExceptionReportSignature(
    [property: JsonRequired] int FormatVersion,
    [property: JsonRequired] string ExceptionType,
    [property: JsonRequired] string StackTrace,
    [property: JsonRequired] string Fingerprint)
{
    public const int CurrentFormatVersion = 1;
    public const int MaximumFrames = 64;
    public const int MaximumStackLength = 4096;
    public const int MaximumMetadataLength = 256;

    public static ExceptionReportSignature? Capture(Exception exception) => CaptureWithOutcome(exception).Signature;

    public static ExceptionReportCapture CaptureWithOutcome(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return CaptureWithOutcome(exception, new StackTrace(exception, fNeedFileInfo: false));
    }

    internal static ExceptionReportCapture CaptureWithOutcome(Exception exception, StackTrace trace)
    {
        if (trace.FrameCount == 0) { return Unavailable(ExceptionReportUnavailableReason.NoManagedStack); }
        if (trace.FrameCount > MaximumFrames) { return Unavailable(ExceptionReportUnavailableReason.LimitExceeded); }
        var exceptionType = exception.GetType().FullName;
        if (!IsMetadata(exceptionType)) { return Unavailable(ExceptionReportUnavailableReason.UnsupportedMetadata); }
        var frames = new StringBuilder();
        for (var index = 0; index < trace.FrameCount; index++)
        {
            var method = trace.GetFrame(index)!.GetMethod();
            if (method?.DeclaringType is not { } declaringType)
            {
                return Unavailable(ExceptionReportUnavailableReason.UnsupportedMetadata);
            }
            // Definitions avoid assembly-qualified generic FullName values, including build versions.
            var typeName = (declaringType.IsGenericType ? declaringType.GetGenericTypeDefinition() : declaringType).FullName;
            if (!IsMetadata(typeName) || !IsMetadata(method.Name))
            {
                return Unavailable(ExceptionReportUnavailableReason.UnsupportedMetadata);
            }
            var parameters = method.GetParameters();
            if (parameters.Length > 32) { return Unavailable(ExceptionReportUnavailableReason.LimitExceeded); }
            var methodName = method.IsGenericMethod
                ? method.Name + "`" + method.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture)
                : method.Name;
            if (!IsMetadata(methodName)) { return Unavailable(ExceptionReportUnavailableReason.UnsupportedMetadata); }
            if (index != 0) { frames.Append('\n'); }
            frames.Append(typeName).Append("::").Append(methodName).Append('(');
            for (var parameter = 0; parameter < parameters.Length; parameter++)
            {
                // Runtime Type.ToString gives type signatures, never parameter names or argument values.
                var parameterType = parameters[parameter].ParameterType.ToString();
                if (!IsTypeSignature(parameterType))
                {
                    return Unavailable(ExceptionReportUnavailableReason.UnsupportedMetadata);
                }
                if (parameter != 0) { frames.Append(';'); }
                frames.Append(parameterType);
            }
            frames.Append(')');
            if (frames.Length > MaximumStackLength) { return Unavailable(ExceptionReportUnavailableReason.LimitExceeded); }
        }
        var stack = frames.ToString();
        return new(Create(exceptionType!, stack),
            ExceptionReportUnavailableReason.None);
    }

    public static ExceptionReportSignature Create(string exceptionType, string normalizedStack)
    {
        if (!IsMetadata(exceptionType) || string.IsNullOrEmpty(normalizedStack)
            || normalizedStack.Length > MaximumStackLength || !IsStack(normalizedStack))
        {
            throw new InvalidDataException("The retained exception report signature is invalid.");
        }
        return new(CurrentFormatVersion, exceptionType, normalizedStack, ComputeFingerprint(exceptionType, normalizedStack));
    }

    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion
            || !string.Equals(Fingerprint, Create(ExceptionType, StackTrace).Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The retained exception report signature is invalid.");
        }
    }

    private static ExceptionReportCapture Unavailable(ExceptionReportUnavailableReason reason) => new(null, reason);

    private static string ComputeFingerprint(string exceptionType, string stack) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(exceptionType + "\n" + stack)));

    private static bool IsMetadata(string? value) => !string.IsNullOrEmpty(value)
        && value.Length <= MaximumMetadataLength && value.All(IsMetadataCharacter);

    private static bool IsMetadataCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '.' or '+' or '`' or '<' or '>' or '|' or '$';

    private static bool IsTypeSignature(string value) => value.Length is > 0 and <= MaximumMetadataLength
        && value.All(character => IsMetadataCharacter(character) || character is '[' or ']' or ',' or '&' or '*');

    private static bool IsStack(string stack)
    {
        var frames = stack.Split('\n');
        if (frames.Length > MaximumFrames) { return false; }
        foreach (var frame in frames)
        {
            var separator = frame.IndexOf("::", StringComparison.Ordinal);
            var parameters = frame.IndexOf('(');
            if (separator <= 0 || parameters <= separator + 2 || !frame.EndsWith(')')
                || !IsMetadata(frame[..separator]) || !IsMetadata(frame[(separator + 2)..parameters]))
            {
                return false;
            }
            var arguments = frame[(parameters + 1)..^1];
            if (arguments.Length == 0) { continue; }
            var types = arguments.Split(';');
            if (types.Length > 32 || types.Any(type => !IsTypeSignature(type))) { return false; }
        }
        return true;
    }
}
