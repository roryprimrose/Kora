using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Kora.Windows.Skills.RuntimeSetup;

internal static partial class WindowsBundledRuntimeSignature
{
    internal static void VerifyMicrosoft(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(),
            Ui = 2, UnionChoice = 1,
            Flags = 0x1000, StateAction = 1, // Cache-only retrieval; retain the exact verified signer until closed.
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        var closeResult = 0;
        try
        {
            Marshal.StructureToPtr(file, pointer, false);
            data.File = pointer;
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            if (WinVerifyTrust(-1, ref action, ref data) != 0)
            { throw new InvalidDataException("The installed PowerShell executable did not pass cached Authenticode trust verification. No network/revocation freshness or runtime qualification is inferred."); }
            var provider = WTHelperProvDataFromStateData(data.State);
            if (provider == 0) { throw new InvalidDataException("Authenticode did not retain its verified signer."); }
            var signerPointer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signerPointer == 0) { throw new InvalidDataException("Authenticode did not identify its verified signer."); }
            var signer = Marshal.PtrToStructure<Signer>(signerPointer);
            if (signer.Count == 0 || signer.Certificates == 0)
            { throw new InvalidDataException("Authenticode returned no verified publisher chain."); }
            var chain = Marshal.PtrToStructure<ProviderCertificate>(signer.Certificates);
            if (chain.Certificate == 0) { throw new InvalidDataException("The verified publisher certificate is missing."); }
            var encoded = Marshal.PtrToStructure<CertificateContext>(chain.Certificate);
            if (encoded.Length is 0 or > 1024 * 1024 || encoded.Bytes == 0)
            { throw new InvalidDataException("The verified publisher certificate is not bounded."); }
            var bytes = new byte[encoded.Length];
            Marshal.Copy(encoded.Bytes, bytes, 0, bytes.Length);
            using var certificate = X509CertificateLoader.LoadCertificate(bytes);
            if (!string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, false), "Microsoft Corporation", StringComparison.Ordinal))
            { throw new InvalidDataException("The installed executable is not signed by the expected Microsoft publisher."); }
        }
        finally
        {
            if (data.State != 0)
            {
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                data.StateAction = 2;
                closeResult = WinVerifyTrust(-1, ref action, ref data);
            }
            Marshal.DestroyStructure<TrustFile>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
        if (closeResult != 0)
        { throw new IOException("The retained native trust verification state did not close successfully."); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Signer
    {
        internal uint Size;
        internal uint VerifyTimeLow;
        internal uint VerifyTimeHigh;
        internal uint Count;
        internal nint Certificates;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCertificate
    {
        internal uint Size;
        internal nint Certificate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CertificateContext
    {
        internal uint Encoding;
        internal nint Bytes;
        internal uint Length;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        internal uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Path;
        internal nint File;
        internal nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        internal uint Size;
        internal nint Policy;
        internal nint Sip;
        internal uint Ui;
        internal uint Revocation;
        internal uint UnionChoice;
        internal nint File;
        internal uint StateAction;
        internal nint State;
        internal nint Url;
        internal uint Flags;
        internal uint Context;
        internal nint Signature;
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint WTHelperProvDataFromStateData(nint state);

    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint WTHelperGetProvSignerFromChain(nint provider, uint signer,
        [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
}
