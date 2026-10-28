using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

public static class BundledActionBinding
{
    public static ExactOperationBinding Create(BundledFixedScript script, string implementationDigest, string identityDigest)
    {
        ArgumentNullException.ThrowIfNull(script);
        var package = script.Package;
        var operation = Digest(new
        {
            Contract = "kora.bundled-fixed-script.v1",
            script.Action,
            package.Manifest.ParameterContract,
            package.ScriptSetDigest,
            package.DeclaredResourceDigest,
        });
        return new(package.Manifest.Action, "bundled", package.Manifest.Id,
            package.DefinitionDigest, package.DeclaredResourceDigest, package.ScriptSetDigest,
            implementationDigest, operation, package.DeclaredResourceDigest, identityDigest,
            Digest("windows.current-interactive-session"),
            Digest("exact-original-script-blocks-then-one-fixed-host-effect.v1"), new HostRevision(1));
    }

    private static string Digest<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}