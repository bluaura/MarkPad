<#
.SYNOPSIS
  Crash-recovery smoke (PRD §5.5): type into a document, wait for a snapshot, kill the process,
  then verify a recovery snapshot exists. Restarting the app should offer to restore it.
#>
param([ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
Add-Type -Name Dpi2 -Namespace MarkPadTools -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
'@
[MarkPadTools.Dpi2]::SetProcessDPIAware() | Out-Null
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\MarkPad.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\win-x64\MarkPad.exe"
$recoveryDir = Join-Path $env:LOCALAPPDATA 'MarkPad\recovery'
Get-ChildItem $recoveryDir -ErrorAction SilentlyContinue | Remove-Item -Force

$work = Join-Path $env:TEMP ("markpad-recovery-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
$file = Join-Path $work 'crash.md'
[IO.File]::WriteAllText($file, "# Crash test`n`nbody`n", (New-Object System.Text.UTF8Encoding($false)))

$proc = Start-Process -FilePath $exe -ArgumentList @("`"$file`"") -PassThru
Start-Sleep -Seconds 8
[MarkPadTools.Dpi2]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[MarkPadTools.Dpi2]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
[MarkPadTools.Dpi2]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 500
if ([MarkPadTools.Dpi2]::GetForegroundWindow() -ne $proc.MainWindowHandle) { Stop-Process -Id $proc.Id -Force; throw 'not foreground' }
$r = New-Object MarkPadTools.Dpi2+RECT; [MarkPadTools.Dpi2]::GetWindowRect($proc.MainWindowHandle, [ref]$r) | Out-Null
[MarkPadTools.Dpi2]::SetCursorPos([int](($r.L + $r.R) / 2), [int]($r.T + 300)) | Out-Null
[MarkPadTools.Dpi2]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [MarkPadTools.Dpi2]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 400
$ws = New-Object -ComObject WScript.Shell
$ws.SendKeys('^{END}'); Start-Sleep -Milliseconds 200
$ws.SendKeys(' UNSAVED'); Start-Sleep -Seconds 7   # > snapshot interval (5s)
Stop-Process -Id $proc.Id -Force   # simulated crash: no normal exit → snapshots must remain
Start-Sleep -Seconds 1

$snaps = Get-ChildItem $recoveryDir -Filter '*.json' -ErrorAction SilentlyContinue
"snapshots after kill: $($snaps.Count)"
foreach ($s in $snaps) {
  $meta = Get-Content $s.FullName -Raw | ConvertFrom-Json
  $text = Get-Content (Join-Path $recoveryDir "$($meta.id).md") -Raw
  "  id=$($meta.id) path=$($meta.path)"
  "  text contains UNSAVED: $($text.Contains('UNSAVED'))"
}
$ok = $snaps.Count -eq 1
"RESULT: " + $(if ($ok) { 'PASS' } else { 'FAIL' })
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
