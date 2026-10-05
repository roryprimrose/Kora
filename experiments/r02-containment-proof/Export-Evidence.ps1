[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Source,
    [Parameter(Mandatory)][string] $Destination
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $Destination) { throw 'Redacted evidence destination must be new.' }
$environment = Get-Content -LiteralPath (Join-Path $Source 'environment.json') -Raw | ConvertFrom-Json
$userSid = $environment.HostToken.UserSid
if ([string]::IsNullOrWhiteSpace($userSid)) { throw 'Host SID missing; cannot safely redact evidence.' }
[void](New-Item -ItemType Directory -Path $Destination)
foreach ($name in 'environment.json', 'trials.json', 'cleanup.json', 'verification.json', 'validation.json') {
    $text = Get-Content -LiteralPath (Join-Path $Source $name) -Raw
    # Preserve real API outcomes, synthetic container SID and timing; remove personal account identity.
    $text.Replace($userSid, 'host-user (redacted)').TrimEnd() |
        Set-Content -LiteralPath (Join-Path $Destination $name) -Encoding utf8
}
