param([Parameter(Mandatory)][System.Action[string]] $SessionControl)

Invoke-KoraFixedSessionAction -Action 'shutdown' -SessionControl $SessionControl
