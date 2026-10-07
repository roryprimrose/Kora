param([Parameter(Mandatory)][System.Action[string]] $SessionControl)

Invoke-KoraFixedSessionAction -Action 'restart' -SessionControl $SessionControl
