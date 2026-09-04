<#
.SYNOPSIS
  Launches the unpackaged Debug build of MarkPad (optionally with a file) and, with -Screenshot,
  captures the main window to a PNG after it has settled. Used for M0 smoke checks without Visual Studio.
#>
param(
    [string]$File,
    [string]$Screenshot,
    # WScript.Shell SendKeys sequence sent after clicking into the document (e.g. '^{END}' to scroll to the end).
    [string]$Keys,
    [int]$SettleSeconds = 8,
    [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
# PowerShell is not DPI aware; without this GetWindowRect/CopyFromScreen return scaled coordinates.
Add-Type -Name Dpi -Namespace MarkPadTools -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
[MarkPadTools.Dpi]::SetProcessDPIAware() | Out-Null
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\MarkPad.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\win-x64\MarkPad.exe"
if (-not (Test-Path $exe)) { throw "Build first: $exe not found" }

if ($File) { $proc = Start-Process -FilePath $exe -ArgumentList @("`"$File`"") -PassThru }
else { $proc = Start-Process -FilePath $exe -PassThru }
Start-Sleep -Seconds $SettleSeconds
$proc.Refresh()
if ($proc.HasExited) { throw "MarkPad exited early with code $($proc.ExitCode)" }
Write-Output "pid=$($proc.Id) title='$($proc.MainWindowTitle)' workingSet=$([math]::Round($proc.WorkingSet64/1MB))MB"

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win32 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
"@
$h = $proc.MainWindowHandle
# ALT press/release around SetForegroundWindow lets a background process take the foreground.
[Win32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[Win32]::SetForegroundWindow($h) | Out-Null
[Win32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 500
if ($Keys -and [Win32]::GetForegroundWindow() -ne $h) {
    Stop-Process -Id $proc.Id -Force
    throw 'MarkPad window is not in the foreground; refusing to send keys to another window.'
}
$r = New-Object Win32+RECT
[Win32]::GetWindowRect($h, [ref]$r) | Out-Null

if ($Keys) {
    $cx = [int](($r.L + $r.R) / 2); $cy = [int]($r.T + 320)
    [Win32]::SetCursorPos($cx, $cy) | Out-Null
    [Win32]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [Win32]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    $ws = New-Object -ComObject WScript.Shell
    $ws.SendKeys($Keys)
    Start-Sleep -Milliseconds 1200
}

if ($Screenshot) {
    Add-Type -AssemblyName System.Drawing
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
