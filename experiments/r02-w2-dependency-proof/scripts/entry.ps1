param([ValidateRange(1, 3)][int] $Value, [string] $Marker)
$fixedValue = Get-W2FixedValue -Value $Value
[IO.File]::WriteAllText($Marker, $fixedValue.ToString([Globalization.CultureInfo]::InvariantCulture))
[pscustomobject]@{ action = 'w2.synthetic.fixed'; value = $fixedValue }
