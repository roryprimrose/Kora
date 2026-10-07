param([Parameter(Mandatory)][System.Action[string]] $SessionControl)

Invoke-KoraFixedSessionAction -Action 'lock' -SessionControl $SessionControl
