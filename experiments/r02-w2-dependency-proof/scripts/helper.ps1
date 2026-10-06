function Get-W2FixedValue {
    param([ValidateRange(1, 3)][int] $Value)
    return (100 + $Value)
}
