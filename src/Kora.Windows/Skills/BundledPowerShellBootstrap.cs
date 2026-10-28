namespace Kora.Windows.Skills;

internal static class BundledPowerShellBootstrap
{
    internal const string Source = """
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $PSModuleAutoloadingPreference = 'None'
        try {
            $document = [System.Text.Json.JsonDocument]::Parse([Console]::In.ReadLine())
            $root = $document.RootElement
            $nonce = $root.GetProperty('Nonce').GetString()
            $invocation = $root.GetProperty('Invocation').GetString()
            $expected = $root.GetProperty('Action').GetString()
            $calls = [System.Runtime.CompilerServices.StrongBox[int]]::new(0)
            $callback = [System.Action[string]] {
                param([string] $action)
                $calls.Value++
                if ($calls.Value -ne 1 -or $action -cne $expected) {
                    throw 'The worker requested an unregistered or repeated fixed effect.'
                }
                [Console]::Out.WriteLine("Kora.FixedAction.Request.v1|$nonce|$invocation|$action")
                [Console]::Out.Flush()
                if ([Console]::In.ReadLine() -cne "Kora.FixedAction.Deferred.v1|$nonce|$invocation") {
                    throw 'The host did not record this exact pending effect request.'
                }
            }.GetNewClosure()
            foreach ($helper in $root.GetProperty('Helpers').EnumerateArray()) {
                . ([scriptblock]::Create($helper.GetProperty('Text').GetString()))
            }
            & ([scriptblock]::Create($root.GetProperty('Entry').GetProperty('Text').GetString())) -SessionControl $callback
            if ($calls.Value -ne 1) { throw 'No exact fixed effect was requested.' }
            [Console]::Out.WriteLine("Kora.FixedAction.Completed.v1|$nonce|$invocation")
            [Console]::Out.Flush()
            $document.Dispose()
            exit 0
        } catch {
            [Console]::Error.WriteLine('The fixed worker did not complete its exact request protocol.')
            exit 1
        }
        """;
}