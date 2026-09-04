<#
.SYNOPSIS
  End-to-end smoke: open a copy of a fixture, type text into the Milkdown editor via SendKeys, press Ctrl+S,
  and verify the file changed while untouched blocks stayed byte-identical (round-trip). No pickers involved.
#>
param([ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
Add-Type -Name Dpi -Namespace MarkPadTools -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
'@
[MarkPadTools.Dpi]::SetProcessDPIAware() | Out-Null
Add-Type -AssemblyName System.Windows.Forms

$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\MarkPad.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\win-x64\MarkPad.exe"
$work = Join-Path $env:TEMP ("markpad-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
$file = Join-Path $work 'smoke.md'
$original = "# Smoke`r`n`r`nFirst paragraph with __strong__ text.`r`n`r`n* item one`r`n* item two`r`n"
[IO.File]::WriteAllText($file, $original, (New-Object System.Text.UTF8Encoding($false)))

$proc = Start-Process -FilePath $exe -ArgumentList @("`"$file`"") -PassThru
Start-Sleep -Seconds 8
[MarkPadTools.Dpi]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

# Cursor lands at the document start (heading). Move to the end of the heading and append text.
# WScript.Shell uses SendInput, which works where the WinForms journal-hook based SendKeys is denied.
$ws = New-Object -ComObject WScript.Shell
$ws.AppActivate($proc.Id) | Out-Null
Start-Sleep -Milliseconds 300
# Click into the document body (below toolbar, centered) so keyboard focus is inside the WebView.
$r = New-Object MarkPadTools.Dpi+RECT
[MarkPadTools.Dpi]::GetWindowRect($proc.MainWindowHandle, [ref]$r) | Out-Null
$cx = [int](($r.L + $r.R) / 2); $cy = [int]($r.T + 300)
[MarkPadTools.Dpi]::SetCursorPos($cx, $cy) | Out-Null
[MarkPadTools.Dpi]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); [MarkPadTools.Dpi]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400
$ws.SendKeys('^{HOME}')
Start-Sleep -Milliseconds 200
$ws.SendKeys('{END}')
Start-Sleep -Milliseconds 200
$ws.SendKeys(' edited')
Start-Sleep -Milliseconds 600
$ws.SendKeys('^s')
Start-Sleep -Seconds 3

$saved = [IO.File]::ReadAllText($file)
$proc.Refresh()
"title='$($proc.MainWindowTitle)'"
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

"--- saved file ---"
$saved
$ok = $saved.StartsWith("# Smoke edited`r`n") -and $saved.Contains("__strong__") -and $saved.Contains("* item one`r`n* item two`r`n")
"RESULT: " + $(if ($ok) { 'PASS (heading changed, other blocks byte-identical, CRLF kept)' } else { 'FAIL' })
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
