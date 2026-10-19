Add-Type -AssemblyName System.Drawing
$assetDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/Journey.Client/public'))
function Draw-Brand($graphics, [single]$x, [single]$y, [single]$size) {
    $pen = [Drawing.Pen]::new([Drawing.Color]::White, $size * 0.065)
    $points = @(3,5, 7,4, 12,5, 16,9, 20,5, 25,4, 29,5, 29,23, 24,22, 20,23, 16,26, 12,23, 7,22, 3,23, 3,5)
    for ($i = 0; $i -lt $points.Count - 2; $i += 2) {
        $graphics.DrawLine($pen, $x+$points[$i]*$size/34, $y+$points[$i+1]*$size/34, $x+$points[$i+2]*$size/34, $y+$points[$i+3]*$size/34)
    }
    $graphics.DrawLine($pen, $x+16*$size/34, $y+9*$size/34, $x+16*$size/34, $y+26*$size/34)
    $pen.Dispose()
}
foreach ($item in @(@(32,'favicon-32.png'), @(180,'apple-touch-icon.png'), @(192,'icon-192.png'), @(512,'icon-512.png'))) {
    $size = [int]$item[0]
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#325ae8'))
    Draw-Brand $graphics ($size*0.15) ($size*0.15) ($size*0.7)
    $bitmap.Save((Join-Path $assetDirectory $item[1]), [Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
}
$bitmap = [Drawing.Bitmap]::new(1200,630)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([Drawing.ColorTranslator]::FromHtml('#18243b'))
$blue = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#325ae8'))
$muted = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#b8c6e0'))
$graphics.FillRectangle($blue, 0, 0, 16, 630)
Draw-Brand $graphics 76 76 90
$font = [Drawing.Font]::new('Arial', 38, [Drawing.FontStyle]::Bold)
$heading = [Drawing.Font]::new('Arial', 51, [Drawing.FontStyle]::Bold)
$caption = [Drawing.Font]::new('Arial', 22)
$graphics.DrawString('openhow', $font, [Drawing.Brushes]::White, 184, 92)
$graphics.DrawString("The story behind`nthe work.", $heading, [Drawing.Brushes]::White, 80, 255)
$graphics.DrawString('Preserve the conversations. Connect the decisions.', $caption, $muted, 84, 505)
$bitmap.Save((Join-Path $assetDirectory 'social-card.png'), [Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose(); $bitmap.Dispose(); $font.Dispose(); $heading.Dispose(); $caption.Dispose(); $blue.Dispose(); $muted.Dispose()
