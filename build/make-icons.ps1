<#
.SYNOPSIS
  Generates the MSIX logo assets (T-44) with System.Drawing: accent rounded square with a white "M".
  Real artwork can replace these files later; names/sizes follow the Package.appxmanifest.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'src\MarkPad.App\Assets\icons'
New-Item -ItemType Directory -Force $out | Out-Null

function New-Logo([int]$w, [int]$h, [string]$name, [bool]$wide = $false) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $size = [Math]::Min($w, $h)
    $pad = [int]($size * 0.08)
    $rect = New-Object System.Drawing.Rectangle (([int](($w - $size) / 2) + $pad), $pad, ($size - 2 * $pad), ($size - 2 * $pad))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [int]($size * 0.18)
    $path.AddArc($rect.X, $rect.Y, $r, $r, 180, 90)
    $path.AddArc($rect.Right - $r, $rect.Y, $r, $r, 270, 90)
    $path.AddArc($rect.Right - $r, $rect.Bottom - $r, $r, $r, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))
    $g.FillPath($brush, $path)
    $font = New-Object System.Drawing.Font ('Segoe UI', [float]($size * 0.52), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString('M', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF ($rect.X, $rect.Y, $rect.Width, $rect.Height)), $fmt)
    $g.Dispose()
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "wrote $name ($w x $h)"
}

New-Logo 44 44 'Square44x44Logo.png'
New-Logo 44 44 'Square44x44Logo.targetsize-44_altform-unplated.png'
New-Logo 150 150 'Square150x150Logo.png'
New-Logo 310 150 'Wide310x150Logo.png' $true
New-Logo 50 50 'StoreLogo.png'
New-Logo 620 300 'SplashScreen.png' $true
New-Logo 256 256 'FileIcon.png'
