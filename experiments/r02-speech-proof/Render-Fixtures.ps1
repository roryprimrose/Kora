[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'fixtures\source'
New-Item -ItemType Directory -Path $output -Force | Out-Null

# File-only SAPI rendering: never attach a microphone or render endpoint.
$phrases = [ordered]@{
    wake = 'Kora'
    immediate = 'Kora, open settings'
    command = 'open settings'
    stop = 'stop speaking'
    tts = 'Kora, open settings. Kora, stop speaking. Say Kora to begin.'
    negative = 'The core of the coral is a quarter of the corridor. Nora and Laura opened settings. Please stop speaking about the weather. We will meet tomorrow.'
}
$voice = New-Object -ComObject SAPI.SpVoice
$receipts = @()
try {
    $voices = $voice.GetVoices()
    for ($i = 0; $i -lt $voices.Count; $i++) {
        $token = $voices.Item($i)
        $description = $token.GetDescription()
        if ($description -notmatch 'English') { continue }
        $voice.Voice = $token
        foreach ($rate in @(-1, 1)) {
            $voice.Rate = $rate
            foreach ($entry in $phrases.GetEnumerator()) {
                $filename = "voice-$i-rate-$rate-$($entry.Key).wav"
                $path = Join-Path $output $filename
                $stream = New-Object -ComObject SAPI.SpFileStream
                try {
                    $stream.Format.Type = 18 # SAFT16kHz16BitMono
                    $stream.Open($path, 3, $false) # SSFMCreateForWrite
                    $voice.AudioOutputStream = $stream
                    $null = $voice.Speak($entry.Value)
                    $voice.AudioOutputStream = $null
                }
                finally {
                    $stream.Close()
                    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($stream) | Out-Null
                }
                $receipts += [ordered]@{
                    file = $filename
                    voice = $description
                    rate = $rate
                    phrase_id = $entry.Key
                    text = $entry.Value
                    sha256 = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
                    provenance = 'Locally authored synthetic text; installed Windows SAPI voice; file-only rendering'
                    redistribution = 'Audio stays local/ignored; Windows voice/software rights are not relicensed by this experiment'
                }
            }
        }
    }
    if ($receipts.Count -eq 0) { throw 'No installed English SAPI voice is available.' }
    $receipts | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'provenance.json') -Encoding utf8
}
finally {
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($voice) | Out-Null
}
Write-Host "Rendered $($receipts.Count) synthetic source files silently in $output"
