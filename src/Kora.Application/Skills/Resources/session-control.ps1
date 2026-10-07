# Definition-only helper. A future admitted host supplies the adapter, never the model.
function Invoke-KoraFixedSessionAction {
    param(
        [Parameter(Mandatory)][ValidateSet('lock', 'shutdown', 'restart')][string] $Action,
        [Parameter(Mandatory)][System.Action[string]] $SessionControl
    )
    if ($null -eq $SessionControl) {
        throw 'No host-admitted fixed session-control adapter is present.'
    }
    $SessionControl.Invoke($Action)
}
