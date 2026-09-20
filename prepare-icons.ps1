# Format conversion and high-quality resampling only; the artwork stays unchanged.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $PSScriptRoot 'assets'
$sourceImage = [System.Drawing.Bitmap]::FromFile((Join-Path $assetRoot 'rigpilot-logo.png'))
function Get-LogoPngBytes([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $canvas = [System.Drawing.Graphics]::FromImage($bitmap)
    $buffer = [System.IO.MemoryStream]::new()
    try {
        $canvas.Clear([System.Drawing.Color]::Transparent)
        $canvas.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $canvas.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $canvas.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $canvas.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $canvas.DrawImage($sourceImage, [System.Drawing.Rectangle]::new(0, 0, $size, $size))
        $bitmap.Save($buffer, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$buffer.ToArray()
    } finally { $buffer.Dispose(); $canvas.Dispose(); $bitmap.Dispose() }
}
try {
    [System.IO.File]::WriteAllBytes((Join-Path $assetRoot 'rigpilot-ui.png'), (Get-LogoPngBytes 384))
    $sizes = @(256, 128, 64, 48, 32, 24, 16)
    $frames = @()
    foreach ($size in $sizes) { $frames += ,(Get-LogoPngBytes $size) }
    $iconStream = [System.IO.File]::Create((Join-Path $assetRoot 'rigpilot.ico'))
    $writer = [System.IO.BinaryWriter]::new($iconStream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        [uint32]$offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            [byte]$dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
            $writer.Write($dimension); $writer.Write($dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length); $writer.Write($offset)
            $offset += [uint32]$frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    } finally { $writer.Dispose(); $iconStream.Dispose() }
} finally { $sourceImage.Dispose() }
Get-ChildItem -LiteralPath $assetRoot -File | Select-Object Name, Length
