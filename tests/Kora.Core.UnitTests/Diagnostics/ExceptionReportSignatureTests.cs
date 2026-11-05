using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Kora.Core.Diagnostics;

namespace Kora.Core.UnitTests.Diagnostics;

public sealed class ExceptionReportSignatureTests
{
    [Fact]
    public void Capture_uses_only_structured_frames_and_ignores_all_exception_text()
    {
        var exception = Thrown(new HostileException());
        var captured = ExceptionReportSignature.CaptureWithOutcome(exception);
        captured.UnavailableReason.Should().Be(ExceptionReportUnavailableReason.None);
        var signature = captured.Signature!;
        ExceptionReportSignature.Capture(exception).Should().Be(signature);
        signature.ExceptionType.Should().Be(typeof(HostileException).FullName);
        signature.FormatVersion.Should().Be(1);
        signature.StackTrace.Should().Be(
            "Kora.Core.UnitTests.Diagnostics.ExceptionReportSignatureTests::Throw(System.Exception)\n" +
            "Kora.Core.UnitTests.Diagnostics.ExceptionReportSignatureTests::Thrown(System.Exception)");
        signature.Fingerprint.Should().Be(Hash(signature.ExceptionType + "\n" + signature.StackTrace));
        signature.Validate();
        var restored = JsonSerializer.Deserialize<ExceptionReportSignature>(JsonSerializer.Serialize(signature))!;
        restored.Should().Be(signature);
        restored.Validate();
    }

    [Fact]
    public void Same_type_and_frames_deduplicate_independent_of_messages_data_and_remote_stack_text()
    {
        var signatures = new List<ExceptionReportSignature>();
        foreach (var message in new[] { "private-one", "private-two" })
        {
            var exception = new IOException(message);
            exception.Data["token"] = "private-value";
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.SetRemoteStackTrace(
                exception, "C:\\private\\source.cs:line 123");
            Thrown(exception);
            signatures.Add(ExceptionReportSignature.Capture(exception)!);
        }
        signatures[0].Should().Be(signatures[1]);
        foreach (var signature in signatures)
        {
            signature.StackTrace.Should().NotContain("\\").And.NotContain("/")
                .And.NotContain(".cs").And.NotContain(":line").And.NotContain("private")
                .And.NotContain("Version=");
        }
        JsonSerializer.Serialize(signatures).Should().NotContain("private").And.NotContain(".cs")
            .And.NotContain("Version=").And.NotContain("\"token\"");
        var differentType = ExceptionReportSignature.Capture(Thrown(new InvalidOperationException()))!;
        differentType.Fingerprint.Should().NotBe(signatures[0].Fingerprint);
        var differentFrame = ExceptionReportSignature.Capture(ThrownElsewhere())!;
        differentFrame.Fingerprint.Should().NotBe(signatures[0].Fingerprint);
    }

    [Fact]
    public void Fingerprints_are_canonical_lowercase_for_creation_capture_and_persisted_validation()
    {
        var signature = ExceptionReportSignature.Create("System.IO.IOException", "Fixture.Worker::Run()");
        signature.Fingerprint.Should().HaveLength(64).And.Be(signature.Fingerprint.ToLowerInvariant());
        signature.Validate();
        var uppercase = signature with { Fingerprint = signature.Fingerprint.ToUpperInvariant() };
        var validate = () => uppercase.Validate();
        validate.Should().Throw<InvalidDataException>();
        var captured = ExceptionReportSignature.Capture(Thrown(new IOException()))!;
        captured.Fingerprint.Should().Be(captured.Fingerprint.ToLowerInvariant());
        captured.Validate();
    }

    [Fact]
    public void No_managed_stack_and_frame_or_text_overflow_are_explicit_unavailable_results()
    {
        ExceptionReportSignature.Capture(new IOException()).Should().BeNull();
        ExceptionReportSignature.CaptureWithOutcome(new IOException()).Should().Be(
            new ExceptionReportCapture(null, ExceptionReportUnavailableReason.NoManagedStack));
        var tooMany = RecursiveException(80);
        ExceptionReportSignature.Capture(tooMany).Should().BeNull();
        ExceptionReportSignature.CaptureWithOutcome(tooMany).Should().Be(
            new ExceptionReportCapture(null, ExceptionReportUnavailableReason.LimitExceeded));
        var tooLong = LongStackException();
        new StackTrace(tooLong, false).FrameCount.Should().BeLessThan(ExceptionReportSignature.MaximumFrames);
        ExceptionReportSignature.Capture(tooLong).Should().BeNull();
        ExceptionReportSignature.CaptureWithOutcome(tooLong).Should().Be(
            new ExceptionReportCapture(null, ExceptionReportUnavailableReason.LimitExceeded));
        var captureNull = () => ExceptionReportSignature.Capture(null!);
        captureNull.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Generic_array_and_reference_parameter_types_have_no_assembly_or_source_information()
    {
        var exception = GenericThrown<Dictionary<string, int[]>>();
        var signature = ExceptionReportSignature.Capture(exception)!;
        signature.Validate();
        signature.StackTrace.Should().Contain("GenericThrow`1(T;System.Int32[];System.String&)")
            .And.NotContain("Version=").And.NotContain(".cs").And.NotContain(":line");
        var nested = ExceptionReportSignature.Capture(GenericOwner<int>.Thrown())!;
        nested.Validate();
        nested.StackTrace.Should().Contain("GenericOwner`1::Thrown()");
    }

    [Fact]
    public void Unsafe_exception_type_is_unavailable_without_reading_exception_content()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new("ExceptionFixture"), AssemblyBuilderAccess.Run);
        var type = assembly.DefineDynamicModule("Fixture").DefineType("bad\\exception", TypeAttributes.Public, typeof(Exception));
        var runtimeType = type.CreateType()!;
        var exception = (Exception)Activator.CreateInstance(runtimeType)!;
        var result = ExceptionReportSignature.CaptureWithOutcome(Thrown(exception));
        result.Should().Be(new ExceptionReportCapture(null, ExceptionReportUnavailableReason.UnsupportedMetadata));
    }

    [Theory]
    [InlineData("bad/type", "Throw", 0)]
    [InlineData("SafeType", "bad\\method", 0)]
    [InlineData("SafeType", "Throw", 33)]
    [InlineData("SafeType", "Throw", 1, true)]
    [InlineData("SafeType", "Throw", 0, false, true)]
    public void Unsupported_metadata_and_excessive_parameters_are_not_truncated(
        string typeName, string methodName, int parameterCount, bool unsafeParameter = false, bool longGenericName = false)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new("SignatureFixture"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Fixture");
        var type = module.DefineType(typeName);
        var parameterType = unsafeParameter ? module.DefineType("bad\\parameter").CreateType()! : typeof(int);
        if (longGenericName) { methodName = new string('M', ExceptionReportSignature.MaximumMetadataLength); }
        var method = type.DefineMethod(methodName, MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), Enumerable.Repeat(parameterType, parameterCount).ToArray());
        if (longGenericName) { method.DefineGenericParameters("T"); }
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Newobj, typeof(IOException).GetConstructor(Type.EmptyTypes)!);
        generator.Emit(OpCodes.Throw);
        var runtimeMethod = type.CreateType()!.GetMethod(methodName)!;
        if (longGenericName) { runtimeMethod = runtimeMethod.MakeGenericMethod(typeof(int)); }
        var invoke = () => runtimeMethod.Invoke(null, new object?[parameterCount]);
        var exception = invoke.Should().Throw<TargetInvocationException>().Which.InnerException!;
        var result = ExceptionReportSignature.CaptureWithOutcome(exception);
        result.Signature.Should().BeNull();
        result.UnavailableReason.Should().Be(parameterCount > 32
            ? ExceptionReportUnavailableReason.LimitExceeded : ExceptionReportUnavailableReason.UnsupportedMetadata);
    }

    [Fact]
    public void Dynamic_method_without_declaring_type_is_unavailable_not_a_partial_stack()
    {
        var method = new DynamicMethod("Throw", typeof(void), Type.EmptyTypes);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Newobj, typeof(IOException).GetConstructor(Type.EmptyTypes)!);
        generator.Emit(OpCodes.Throw);
        var action = method.CreateDelegate<Action>();
        var exception = action.Should().Throw<IOException>().Which;
        ExceptionReportSignature.Capture(exception).Should().BeNull();
        ExceptionReportSignature.CaptureWithOutcome(exception).Should().Be(
            new ExceptionReportCapture(null, ExceptionReportUnavailableReason.UnsupportedMetadata));
    }

    [Fact]
    public void Frame_without_method_metadata_is_unavailable_without_disclosure()
    {
        var frame = new MissingMethodFrame();
        var trace = new StackTrace(frame);
        trace.FrameCount.Should().Be(1);
        trace.GetFrame(0).Should().BeSameAs(frame);
        trace.GetFrame(0)!.GetMethod().Should().BeNull();
        var result = ExceptionReportSignature.CaptureWithOutcome(new IOException("private-message"), trace);
        result.Should().Be(new ExceptionReportCapture(null, ExceptionReportUnavailableReason.UnsupportedMetadata));
        JsonSerializer.Serialize(result).Should().NotContain("private").And.NotContain(".cs").And.NotContain("17");
    }

    [Theory]
    [InlineData(0, "System.Exception", "A::M()", false)]
    [InlineData(2, "System.Exception", "A::M()", false)]
    [InlineData(1, null, "A::M()", false)]
    [InlineData(1, "", "A::M()", false)]
    [InlineData(1, "bad\\type", "A::M()", false)]
    [InlineData(1, "System.Exception", null, false)]
    [InlineData(1, "System.Exception", "", false)]
    [InlineData(1, "System.Exception", "A::M()", true)]
    [InlineData(1, "System.Exception", "A::M()\n", false)]
    [InlineData(1, "System.Exception", "A.M()", false)]
    [InlineData(1, "System.Exception", "::M()", false)]
    [InlineData(1, "System.Exception", "A::()", false)]
    [InlineData(1, "System.Exception", "A::M(", false)]
    [InlineData(1, "System.Exception", "A::M)", false)]
    [InlineData(1, "System.Exception", "bad/type::M()", false)]
    [InlineData(1, "System.Exception", "A::bad\\method()", false)]
    [InlineData(1, "System.Exception", "A::M(System.Int32;)", false)]
    [InlineData(1, "System.Exception", "A::M(C:\\private\\source.cs)", false)]
    [InlineData(1, "System.Exception", "A::M(System.Int32,Assembly,Version=1)", false)]
    public void Persisted_signature_rejects_bad_formats_metadata_frames_and_fingerprints(
        int version, string? type, string? stack, bool badHash)
    {
        var signature = new ExceptionReportSignature(version, type!, stack!,
            badHash ? new string('0', 64) : Hash(type + "\n" + stack));
        var validate = () => signature.Validate();
        validate.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Persisted_signature_enforces_all_bounds_without_truncating()
    {
        var valid = new ExceptionReportSignature(1, "System.Exception", "A::M(System.Int32;System.String[])",
            Hash("System.Exception\nA::M(System.Int32;System.String[])"));
        valid.Validate();
        foreach (var stack in new[]
        {
            new string('X', ExceptionReportSignature.MaximumStackLength + 1),
            string.Join('\n', Enumerable.Repeat("A::M()", ExceptionReportSignature.MaximumFrames + 1)),
            "A::M(" + string.Join(';', Enumerable.Repeat("System.Int32", 33)) + ")",
            "A::M(" + new string('X', ExceptionReportSignature.MaximumMetadataLength + 1) + ")",
        })
        {
            var oversized = valid with { StackTrace = stack, Fingerprint = Hash(valid.ExceptionType + "\n" + stack) };
            var validate = () => oversized.Validate();
            validate.Should().Throw<InvalidDataException>();
        }
        var longType = valid with { ExceptionType = new string('X', ExceptionReportSignature.MaximumMetadataLength + 1) };
        var validateType = () => longType.Validate();
        validateType.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Persisted_signature_accepts_exact_metadata_frame_parameter_and_stack_bounds()
    {
        var maximumType = new string('E', ExceptionReportSignature.MaximumMetadataLength);
        var maximumStack = "A::" + new string('M', 251) + "()\n"
            + string.Join('\n', Enumerable.Repeat("A::" + new string('M', 250) + "()", 15));
        maximumStack.Length.Should().Be(ExceptionReportSignature.MaximumStackLength);
        var signature = ExceptionReportSignature.Create(maximumType, maximumStack);
        signature.Fingerprint.Should().Be(Hash(maximumType + "\n" + maximumStack));
        signature.Validate();
        foreach (var stack in new[]
        {
            string.Join('\n', Enumerable.Repeat("A::M()", ExceptionReportSignature.MaximumFrames)),
            "A::M(" + string.Join(';', Enumerable.Repeat("System.Int32", 32)) + ")",
            "A::" + new string('M', ExceptionReportSignature.MaximumMetadataLength) + "()",
            "A::M(" + new string('T', ExceptionReportSignature.MaximumMetadataLength) + ")",
            "Namespace.Type+<Generated>$_|`1::M`1(System.Int32[,];System.Int32*;System.String&;Generic.Type`1[System.Int32])",
        })
        {
            (signature with { StackTrace = stack, Fingerprint = Hash(maximumType + "\n" + stack) }).Validate();
        }
    }

    [Theory]
    [InlineData("FormatVersion")]
    [InlineData("ExceptionType")]
    [InlineData("StackTrace")]
    [InlineData("Fingerprint")]
    public void Persisted_signature_fields_are_required(string field)
    {
        var node = JsonNode.Parse("""{"FormatVersion":1,"ExceptionType":"System.Exception","StackTrace":"A::M()","Fingerprint":"hash"}""")!.AsObject();
        node.Remove(field);
        var read = () => JsonSerializer.Deserialize<ExceptionReportSignature>(node.ToJsonString());
        read.Should().Throw<JsonException>();
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Throw(Exception exception) => throw exception;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception Thrown(Exception exception)
    {
        try { Throw(exception); }
        catch (Exception caught) { return caught; }
        throw new UnreachableException();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception ThrownElsewhere()
    {
        try { throw new IOException(); }
        catch (IOException caught) { return caught; }
    }

    private static Exception RecursiveException(int depth)
    {
        try { Recurse(depth); }
        catch (IOException caught) { return caught; }
        throw new UnreachableException();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Recurse(int depth)
    {
        if (depth == 0) { throw new IOException(); }
        Recurse(depth - 1);
        GC.KeepAlive(depth);
    }

    private static Exception LongStackException()
    {
        try { LongRecurse(45, null, null, null); }
        catch (IOException caught) { return caught; }
        throw new UnreachableException();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LongRecurse(int depth, ExceptionReportSignatureTests? first,
        ExceptionReportSignatureTests? second, ExceptionReportSignatureTests? third)
    {
        if (depth == 0) { throw new IOException(); }
        LongRecurse(depth - 1, first, second, third);
        GC.KeepAlive(depth);
    }

    private static Exception GenericThrown<T>()
    {
        var value = "";
        try { GenericThrow(default(T), [], ref value); }
        catch (IOException caught) { return caught; }
        throw new UnreachableException();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GenericThrow<T>(T? value, int[] numbers, ref string text) => throw new IOException();

    private sealed class MissingMethodFrame : StackFrame
    {
        public MissingMethodFrame() : base("private-source.cs", 17) { }

        public override MethodBase? GetMethod() => null;
    }

    private sealed class HostileException : Exception
    {
        public override string Message => throw new InvalidOperationException("Message must not be read.");
        public override string StackTrace => throw new InvalidOperationException("StackTrace must not be read.");
        public override string ToString() => throw new InvalidOperationException("ToString must not be read.");
    }

    private static class GenericOwner<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Exception Thrown()
        {
            try { throw new IOException(); }
            catch (IOException caught) { return caught; }
        }
    }
}
