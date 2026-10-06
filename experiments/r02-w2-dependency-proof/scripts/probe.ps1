$ErrorActionPreference = 'Stop'
$spec = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:W2_SPEC)) | ConvertFrom-Json
$blocks = @{}
foreach ($file in $spec.Scripts) {
    $text = [Text.UTF8Encoding]::new($false, $true).GetString([Convert]::FromBase64String($file.Bytes))
    $blocks[$file.Name] = [scriptblock]::Create($text)
}
# Helpers are distinct, definition-only blocks in the entry invocation's scope.
$result = & {
    . $blocks['scripts\helper.ps1']
    & $blocks['scripts\entry.ps1'] -Value 2 -Marker ($spec.Receipt + '.effect')
}
if ($spec.Mode -eq 'lost') { exit 0 }
if ($spec.Mode -eq 'malformed') { [IO.File]::WriteAllText($spec.Receipt, '{invalid'); exit 0 }
if ($spec.Mode -eq 'cancel') { [Threading.Thread]::Sleep(120000); exit 0 }
$effect = @{
    RunId = $spec.RunId; Pid = $PID; AdmissionDigest = $spec.AdmissionDigest
    Value = $result.value
}
$observations = [Collections.Generic.List[object]]::new()
function Test-W2Load([string] $name, [scriptblock] $body) {
    try {
        $value = & $body
        $observations.Add(@{ Name = $name; Outcome = 'Allowed'; Value = $value; Error = $null })
    }
    catch {
        $failure = $_.Exception
        while ($null -ne $failure.InnerException) { $failure = $failure.InnerException }
        $code = $failure.HResult -band 0xffff
        $observations.Add(@{
            Name = $name; Outcome = $(if ($failure -is [UnauthorizedAccessException]) { 'Denied' } else { 'Unknown' })
            Value = $null; Error = $_.Exception.GetType().FullName
            NativeException = $failure.GetType().FullName; NativeError = $code
        })
    }
}
Test-W2Load 'denied.script' { & $spec.DeniedScript }
Test-W2Load 'denied.script.read' { [IO.File]::ReadAllText($spec.DeniedScript) }
Test-W2Load 'writable.script' { & $spec.WritableScript }
Test-W2Load 'writable.module.explicit' { Import-Module $spec.Module -Force; Get-W2Undeclared }
$env:PSModulePath = $spec.ModuleRoot
Remove-Module W2Undeclared -ErrorAction Stop
$PSModuleAutoLoadingPreference = 'All'
Test-W2Load 'writable.module.autoload' { Get-W2Undeclared }
Test-W2Load 'writable.managed.bytes' {
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($spec.Payload))
    $assembly.GetType('W2Payload.FixedEffect').GetMethod('Observe').Invoke($null, @())
}
$receipt = @{
    RunId = $spec.RunId; Pid = $PID
    Token = ([Reflection.Assembly]::LoadFile($spec.DeclaredPayload).GetType('W2Payload.FixedEffect').
        GetMethod('TokenJson').Invoke($null, @()) | ConvertFrom-Json)
    Framework = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
    Version = $PSVersionTable.PSVersion.ToString()
    LanguageMode = $ExecutionContext.SessionState.LanguageMode.ToString()
    Effect = $effect; Probes = $observations
}
$json = $receipt | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($spec.Receipt + '.tmp', $json)
[IO.File]::Move($spec.Receipt + '.tmp', $spec.Receipt)
$images = [Reflection.Assembly]::LoadFile($spec.DeclaredPayload).GetType('W2Payload.FixedEffect').
    GetMethod('ImagesJson').Invoke($null, @())
[IO.File]::WriteAllText((Join-Path ([IO.Path]::GetDirectoryName($spec.Receipt)) 'loaded-images.json'), $images)
