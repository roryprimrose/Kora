using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Kora.Architecture;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PortableBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "KORAARCH002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Keep operating-system mechanisms behind the platform seam",
        "'{0}' is a Windows/native mechanism and cannot be consumed by a portable Kora layer",
        "Architecture",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        customTags: [WellKnownDiagnosticTags.NotConfigurable]);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterSyntaxNodeAction(
            AnalyzeName,
            SyntaxKind.IdentifierName,
            SyntaxKind.GenericName);
        context.RegisterSymbolAction(AnalyzeDeclaration, SymbolKind.Method, SymbolKind.NamedType);
        context.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol;
        if (symbol is not null && IsPlatformMechanism(symbol))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), symbol.ToDisplayString()));
        }
    }

    private static void AnalyzeDeclaration(SymbolAnalysisContext context)
    {
        if (context.Symbol is IMethodSymbol { } method && method.GetDllImportData() is not null
            || context.Symbol is INamedTypeSymbol { IsComImport: true }
            || HasWindowsRequirement(context.Symbol))
        {
            foreach (var location in context.Symbol.Locations.Where(static location => location.IsInSource))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, context.Symbol.ToDisplayString()));
            }
        }
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        // Catching an adapter's managed error is portable; creating this exception can read native error state.
        if (creation.Type is { } type
            && string.Equals(type.ToDisplayString(), "System.ComponentModel.Win32Exception", StringComparison.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation(), type.ToDisplayString()));
        }
    }

    private static bool IsPlatformMechanism(ISymbol symbol)
    {
        for (ISymbol? current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (HasWindowsRequirement(current))
            {
                return true;
            }

            if (current is not INamedTypeSymbol type)
            {
                continue;
            }

            var space = type.ContainingNamespace.ToDisplayString();
            if (IsNamespace(space, "Microsoft.Win32")
                || IsNamespace(space, "System.Management")
                || IsNamespace(space, "System.Speech")
                || IsNamespace(space, "Windows")
                || IsNamespace(space, "WinRT")
                || IsNamespace(space, "Kora.Windows"))
            {
                return true;
            }

            if (space.Equals("System.Runtime.InteropServices", StringComparison.Ordinal) && type.Name is
                "DllImportAttribute" or "LibraryImportAttribute" or "ComImportAttribute"
                or "UnmanagedCallersOnlyAttribute" or "UnmanagedFunctionPointerAttribute"
                or "Marshal" or "NativeLibrary" or "NativeMemory" or "ComWrappers"
                or "SafeHandle" or "CriticalHandle" or "SafeBuffer" or "HandleRef")
            {
                return true;
            }

            if (space.Equals("System.Runtime.InteropServices.Marshalling", StringComparison.Ordinal)
                && type.Name is "GeneratedComInterfaceAttribute" or "GeneratedComClassAttribute" or "ComInterfaceMarshaller")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasWindowsRequirement(ISymbol symbol) =>
        symbol.GetAttributes().Any(static attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.Runtime.Versioning.SupportedOSPlatformAttribute", StringComparison.Ordinal)
            && attribute.ConstructorArguments is [{ Value: string platform }]
            && platform.StartsWith("windows", StringComparison.OrdinalIgnoreCase));

    private static bool IsNamespace(string value, string prefix) =>
        value.Equals(prefix, StringComparison.Ordinal) || value.StartsWith(prefix + ".", StringComparison.Ordinal);
}
