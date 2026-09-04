# PostToolUse(Edit|Write|MultiEdit): 편집된 파일 종류에 따라 빠른 검사와 알림을 수행한다.
# - .ts/.tsx : tsc --noEmit (증분, 수 초) → 오류 시 Claude에게 보고
# - .cs      : 빌드는 느리므로 여기서는 하지 않음(Stop 훅에서 수행). 금지 API 사용만 grep.
# - 브릿지 파일: 동기화 대상 3곳을 상기시킨다.
$ErrorActionPreference = 'SilentlyContinue'
$root = $env:CLAUDE_PROJECT_DIR
$input_json = [Console]::In.ReadToEnd()
try { $payload = $input_json | ConvertFrom-Json } catch { exit 0 }
$file = [string]$payload.tool_input.file_path
if (-not $file) { exit 0 }
$rel = $file.Replace($root, '').TrimStart('\','/')
$msgs = @()

# 1) 브릿지 동기화 상기
$bridgeFiles = @('Bridge\BridgeMessages.cs','Bridge/BridgeMessages.cs','bridge-types.ts','src/handlers/','src\handlers\','bridge.ts','EditorBridge.cs')
foreach ($b in $bridgeFiles) {
  if ($rel -like "*$b*") {
    $msgs += "브릿지 관련 파일($rel) 변경됨. 같은 커밋에서 BridgeMessages.cs / bridge-types.ts / docs/ARCHITECTURE.md §4 표를 함께 갱신했는지 확인하고, 'pwsh scripts/check-bridge-sync.ps1' 를 실행하세요."
    break
  }
}

# 2) C#: WASDK 1.x 관용구·직접 파일 쓰기 감지
if ($rel -like '*.cs') {
  $content = Get-Content $file -Raw
  if ($content -match 'Window\.Current\b')            { $msgs += "$rel : 'Window.Current' 는 Windows App SDK 2.x에서 폐기됨. App.MainWindow/DI로 교체." }
  if ($content -match '\.Dispatcher\b' -and $content -notmatch 'DispatcherQueue') { $msgs += "$rel : 'DependencyObject.Dispatcher' 폐기됨. DispatcherQueue 사용." }
  if ($rel -like 'src\MarkPad.Core*' -or $rel -like 'src/MarkPad.Core*') {
    if ($content -match 'File\.WriteAll(Text|Bytes|Lines)') { $msgs += "$rel : Core에서 File.WriteAll* 직접 호출 금지. AtomicWriter 사용." }
    if ($content -match 'using\s+(Microsoft\.UI|Windows\.UI|WinRT)') { $msgs += "$rel : MarkPad.Core는 UI 네임스페이스를 참조하면 안 됨." }
  }
}

# 3) TS: 타입 검사 (증분)
if ($rel -like '*.ts' -and $rel -like '*MarkPad.Editor.Web*' -and $rel -notlike '*node_modules*') {
  $web = Join-Path $root 'src\MarkPad.Editor.Web'
  if (Test-Path (Join-Path $web 'node_modules')) {
    Push-Location $web
    $out = & npx tsc --noEmit --pretty false 2>&1 | Select-Object -First 20
    Pop-Location
    if ($LASTEXITCODE -ne 0) { $msgs += "tsc --noEmit 실패:`n" + ($out -join "`n") }
  }
}

# 4) 산출물 직접 편집 감지
if ($rel -like '*MarkPad.App\Assets\editor*' -or $rel -like '*MarkPad.App/Assets/editor*') {
  $msgs += "$rel 은 빌드 산출물입니다. 원본(src/MarkPad.Editor.Web/src)을 수정하고 npm run build 하세요."
}

if ($msgs.Count -gt 0) {
  $ctx = ($msgs -join "`n")
  $obj = @{ hookSpecificOutput = @{ hookEventName = 'PostToolUse'; additionalContext = $ctx } }
  Write-Output ($obj | ConvertTo-Json -Depth 4 -Compress)
}
exit 0
