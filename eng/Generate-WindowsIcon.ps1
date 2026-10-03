[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\src\Kora.Desktop\Assets\Kora.ico')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing.Common

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$palette = [System.Drawing.Color[]] @(
    [System.Drawing.ColorTranslator]::FromHtml('#F49C9C'),
    [System.Drawing.ColorTranslator]::FromHtml('#F0CC83'),
    [System.Drawing.ColorTranslator]::FromHtml('#9BDFAC'),
    [System.Drawing.ColorTranslator]::FromHtml('#6AE1DA'),
    [System.Drawing.ColorTranslator]::FromHtml('#B8D9EC'),
    [System.Drawing.ColorTranslator]::FromHtml('#80B7FF'),
    [System.Drawing.ColorTranslator]::FromHtml('#AF9BFF')
)
$positions = [single[]] @(0, 0.18, 0.34, 0.49, 0.60, 0.77, 1)

function New-IconPng {
    param([int] $Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $gradient = $null
    $pen = $null
    $highlightPen = $null
    $stream = [System.IO.MemoryStream]::new()

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

        $margin = [Math]::Max(1, $Size * 0.06)
        $scale = ($Size - (2 * $margin)) / 264
        $offsetX = ($Size / 2) - (128 * $scale)
        $offsetY = $margin - (28 * $scale)

        function Convert-Point([double] $X, [double] $Y) {
            [System.Drawing.PointF]::new(
                [single] ($offsetX + ($X * $scale)),
                [single] ($offsetY + ($Y * $scale)))
        }

        $path.StartFigure()
        $path.AddBezier(
            (Convert-Point 128 28),
            (Convert-Point 78 28),
            (Convert-Point 52 49),
            (Convert-Point 52 88))
        $path.AddBezier(
            (Convert-Point 52 88),
            (Convert-Point 52 119),
            (Convert-Point 79 141),
            (Convert-Point 128 160))
        $path.AddBezier(
            (Convert-Point 128 160),
            (Convert-Point 177 179),
            (Convert-Point 204 201),
            (Convert-Point 204 232))
        $path.AddBezier(
            (Convert-Point 204 232),
            (Convert-Point 204 271),
            (Convert-Point 178 292),
            (Convert-Point 128 292))
        $path.AddBezier(
            (Convert-Point 128 292),
            (Convert-Point 78 292),
            (Convert-Point 52 271),
            (Convert-Point 52 232))
        $path.AddBezier(
            (Convert-Point 52 232),
            (Convert-Point 52 201),
            (Convert-Point 79 179),
            (Convert-Point 128 160))
        $path.AddBezier(
            (Convert-Point 128 160),
            (Convert-Point 177 141),
            (Convert-Point 204 119),
            (Convert-Point 204 88))
        $path.AddBezier(
            (Convert-Point 204 88),
            (Convert-Point 204 49),
            (Convert-Point 178 28),
            (Convert-Point 128 28))

        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new(0, [single] $margin),
            [System.Drawing.PointF]::new(0, [single] ($Size - $margin)),
            $palette[0],
            $palette[-1])
        $blend = [System.Drawing.Drawing2D.ColorBlend]::new($palette.Length)
        $blend.Colors = $palette
        $blend.Positions = $positions
        $gradient.InterpolationColors = $blend

        $pen = [System.Drawing.Pen]::new($gradient, [single] (27 * $scale))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawPath($pen, $path)

        if ($Size -ge 32) {
            $highlightPen = [System.Drawing.Pen]::new(
                [System.Drawing.Color]::FromArgb(36, 255, 255, 255),
                [single] [Math]::Max(1, 5 * $scale))
            $highlightPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $highlightPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $highlightPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $graphics.DrawPath($highlightPen, $path)
        }

        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
        if ($null -ne $highlightPen) {
            $highlightPen.Dispose()
        }

        if ($null -ne $pen) {
            $pen.Dispose()
        }

        if ($null -ne $gradient) {
            $gradient.Dispose()
        }
        $path.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$frames = @($sizes | ForEach-Object {
    [PSCustomObject] @{
        Size = $_
        Data = New-IconPng -Size $_
    }
})

$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutputPath)) | Out-Null

$file = [System.IO.File]::Create($resolvedOutputPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16] 0)
    $writer.Write([uint16] 1)
    $writer.Write([uint16] $frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte] $dimension)
        $writer.Write([byte] $dimension)
        $writer.Write([byte] 0)
        $writer.Write([byte] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] 32)
        $writer.Write([uint32] $frame.Data.Length)
        $writer.Write([uint32] $offset)
        $offset += $frame.Data.Length
    }

    foreach ($frame in $frames) {
        $data = [byte[]] $frame.Data
        $writer.Write($data, 0, $data.Length)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output "Generated $resolvedOutputPath with $($frames.Count) PNG frames."
