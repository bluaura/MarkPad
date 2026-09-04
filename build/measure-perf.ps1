<#
.SYNOPSIS
  T-11 / T-43 performance measurement on the Release build: cold start → editor ready, doc.load for 100KB and
  1MB fixtures, and memory of MarkPad + its WebView2 process tree. Prints a markdown table.
#>
param([ValidateSet('Debug', 'Release')] [string]$Configuration = 'Release', [int]$SettleSeconds = 12)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root "src\MarkPad.App\bin\x64\$Configuration\net10.0-windows10.0.19041.0\win-x64\MarkPad.exe"
$log = (Get-ChildItem "$env:LOCALAPPDATA\MarkPad\logs\*.log" | Sort-Object LastWriteTime | Select-Object -Last 1).FullName

# fixtures: concatenate corpus files to ~100KB and ~1MB
$work = Join-Path $env:TEMP 'markpad-perf'; New-Item -ItemType Directory -Force $work | Out-Null
$corpus = Get-ChildItem (Join-Path $root 'corpus\roundtrip\*.md')
function Build-Fixture([string]$name, [int]$bytes) {
    $path = Join-Path $work $name
    $sb = New-Object System.Text.StringBuilder
    while ($sb.Length -lt $bytes) { foreach ($f in $corpus) { [void]$sb.Append([IO.File]::ReadAllText($f.FullName)).Append("`n"); if ($sb.Length -ge $bytes) { break } } }
    [IO.File]::WriteAllText($path, $sb.ToString().Substring(0, $bytes), (New-Object System.Text.UTF8Encoding($false)))
    return $path
}
$f100 = Build-Fixture 'doc-100kb.md' 102400
$f1m = Build-Fixture 'doc-1mb.md' 1048576

function Get-Tree([int]$rootPid) {
    $all = Get-CimInstance Win32_Process
    $ids = New-Object System.Collections.Generic.HashSet[int]; [void]$ids.Add($rootPid)
    do { $added = $false; foreach ($w in $all) { if ($ids.Contains([int]$w.ParentProcessId) -and -not $ids.Contains([int]$w.ProcessId)) { [void]$ids.Add([int]$w.ProcessId); $added = $true } } } while ($added)
    $ws = 0; $priv = 0; $n = 0
    foreach ($id in $ids) { $p = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($p) { $ws += $p.WorkingSet64; $priv += $p.PrivateMemorySize64; $n++ } }
    return @{ ws = [math]::Round($ws / 1MB); priv = [math]::Round($priv / 1MB); n = $n }
}

$rows = @()
foreach ($case in @(@{ n = 'empty (cold)'; f = $null }, @{ n = '100KB'; f = $f100 }, @{ n = '1MB'; f = $f1m }, @{ n = 'empty (warm)'; f = $null })) {
    $before = (Get-Content $log).Count
    if ($case.f) { $p = Start-Process -FilePath $exe -ArgumentList @("`"$($case.f)`"") -PassThru } else { $p = Start-Process -FilePath $exe -PassThru }
    Start-Sleep -Seconds $SettleSeconds
    $tree = Get-Tree $p.Id
    $new = (Get-Content $log) | Select-Object -Skip $before
    $ready = $new | Select-String 'ready after (\d+) ms \(process uptime (\d+) ms\)' | Select-Object -First 1
    $load = $new | Select-String 'doc.load (\d+) chars in (\d+) ms' | Select-Object -First 1
    $rows += [pscustomobject]@{
        case = $case.n
        uptimeAtReady_ms = $(if ($ready) { $ready.Matches[0].Groups[2].Value } else { '-' })
        docLoad_ms = $(if ($load) { $load.Matches[0].Groups[2].Value } else { '-' })
        chars = $(if ($load) { $load.Matches[0].Groups[1].Value } else { '-' })
        procs = $tree.n
        workingSet_MB = $tree.ws
        private_MB = $tree.priv
    }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}
"| 케이스 | 시작→ready (ms) | doc.load (ms) | 문자 수 | 프로세스 | 작업 집합 합계 (MB) | 프라이빗 합계 (MB) |"
"|---|---|---|---|---|---|---|"
foreach ($r in $rows) { "| $($r.case) | $($r.uptimeAtReady_ms) | $($r.docLoad_ms) | $($r.chars) | $($r.procs) | $($r.workingSet_MB) | $($r.private_MB) |" }
$bundle = Get-ChildItem (Join-Path $root 'src\MarkPad.App\Assets\editor\editor.js')
"editor.js: $([math]::Round($bundle.Length / 1KB)) KB"
