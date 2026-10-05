$ErrorActionPreference = 'Stop'
$result = [ordered]@{
    version = $PSVersionTable.PSVersion.ToString()
    allowed = [IO.File]::ReadAllText($env:KORA_ALLOWED)
    protectedWrite = 'Unknown'
    protectedError = $null
    network = 'Unknown'
    networkError = $null
    networkLan = 'Unknown'
    networkLanError = $null
}
try {
    [IO.File]::AppendAllText($env:KORA_PROTECTED, 'synthetic-powershell-write')
    $result.protectedWrite = 'Allowed'
} catch [UnauthorizedAccessException] {
    $result.protectedWrite = 'Denied'
    $result.protectedError = $_.Exception.HResult
}
foreach ($endpoint in @(
    @{ Address = '127.0.0.1'; Outcome = 'network'; Error = 'networkError' },
    @{ Address = $env:KORA_ADDRESS; Outcome = 'networkLan'; Error = 'networkLanError' }
)) {
    $client = [Net.Sockets.TcpClient]::new()
    try {
        $connected = $client.ConnectAsync($endpoint.Address, [int]$env:KORA_PORT).Wait(3000)
        if ($connected) { $result[$endpoint.Outcome] = 'Allowed' }
    } catch [AggregateException] {
        $socketError = $_.Exception.InnerException
        if ($socketError -isnot [Net.Sockets.SocketException]) { throw }
        $result[$endpoint.Error] = $socketError.NativeErrorCode
        $result[$endpoint.Outcome] = if ($socketError.NativeErrorCode -eq 10013) { 'Denied' } else { 'Unknown' }
    } finally {
        $client.Dispose()
    }
}
$result | ConvertTo-Json -Compress
