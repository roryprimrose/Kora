[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$original = [Threading.Thread]::CurrentThread.CurrentCulture
try
{
    foreach ($culture in @('en-AU', 'en-US'))
    {
        [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo($culture)
        $json = '{"startedUtc":"2026-10-06T06:33:32.2415340+00:00"}' | ConvertFrom-Json -DateKind String
        $parsed = [DateTimeOffset]::Parse($json.startedUtc, [Globalization.CultureInfo]::InvariantCulture)
        if ($parsed.Year -ne 2026 -or $parsed.Month -ne 10 -or $parsed.Day -ne 6 -or $parsed.Offset -ne [TimeSpan]::Zero)
        {
            throw "UTC receipt changed with locale $culture."
        }
    }
}
finally { [Threading.Thread]::CurrentThread.CurrentCulture = $original }
Write-Host '2/2 receipt locale contracts passed: ISO UTC strings stay UTC before freshness checks.'
