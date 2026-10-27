param([string]$Output = (Join-Path $PSScriptRoot '..\..\src\M1vClock.App\Assets\m1vClock.ico'))
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $radius = [Math]::Max(3, [int]($size * 0.22))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $top = [System.Drawing.Color]::FromArgb(255, 0x25, 0x69, 0xD9)
    $bottom = [System.Drawing.Color]::FromArgb(255, 0x0A, 0x3F, 0x9E)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $size), $top, $bottom
    $g.FillPath($brush, $path)
    $text = if ($size -le 24) { 'm' } else { 'm1v' }
    $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($size * ($(if ($size -le 24) { 0.62 } else { 0.36 })))), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = 'Center'
    $format.LineAlignment = 'Center'
    $rect = New-Object System.Drawing.RectangleF 0, ([single](-$size * 0.03)), $size, $size
    $g.DrawString($text, $font, [System.Drawing.Brushes]::White, $rect, $format)
    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$side)
    $writer.Write([byte]$side)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($Output), $out.ToArray())
"icone: $Output ($($out.Length) bytes)"
