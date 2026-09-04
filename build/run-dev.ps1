<#
.SYNOPSIS
  Launches the unpackaged Debug build of MarkPad (optionally with a file) and, with -Screenshot,
  captures the main window to a PNG after it has settled. Used for M0 smoke checks without Visual Studio.
#>
param(
    [string]$File,
    [string]$Screenshot,
    [int]$SettleSeconds = 8,
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\MarkPad.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\win-x64\MarkPad.exe"
if (-not (Test-Path $exe)) { throw "Build first: $exe not found" }

if ($File) { $proc = Start-Process -FilePath $exe -ArgumentList @("`"$File`"") -PassThru }
else { $proc = Start-Process -FilePath $exe -PassThru }
Start-Sleep -Seconds $SettleSeconds
$proc.Refresh()
if ($proc.HasExited) { throw "MarkPad exited early with code $($proc.ExitCode)" }
Write-Output "pid=$($proc.Id) title='$($proc.MainWindowTitle)' workingSet=$([math]::Round($proc.WorkingSet64/1MB))MB"

if ($Screenshot) {
    Add-Type -AssemblyName System.Drawing
    Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win32 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
    $h = $proc.MainWindowHandle
    [Win32]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 500
    $r = New-Object Win32+RECT
    [Win32]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $hgt = $r.B - $r.T
    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $bmp.Save($Screenshot, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output "screenshot=$Screenshot ($w x $hgt)"
}

if (-not $KeepRunning) {
    Stop-Process -Id $proc.Id -Force
    Write-Output 'stopped'
}
