# Stop: 코드가 변경된 세션이면 빠른 검증을 돌리고, 실패 시 exit 2 로 Claude를 계속 일하게 한다.
# 무한 루프 방지: stop_hook_active 가 true 면(이미 이 훅 때문에 계속 중) 한 번만 더 허용하고 그 다음은 통과.
$ErrorActionPreference = 'SilentlyContinue'
$root = $env:CLAUDE_PROJECT_DIR
$input_json = [Console]::In.ReadToEnd()
try { $payload = $input_json | ConvertFrom-Json } catch { $payload = $null }

# 코드 변경이 없으면 검증 생략 (문서만 고친 세션 등)
$changed = git -C $root status --porcelain -- src tests 2>$null
if (-not $changed) { exit 0 }

$marker = Join-Path $root '.claude\.stop-retry'
if ($payload -and $payload.stop_hook_active -eq $true) {
  if (Test-Path $marker) { Remove-Item $marker -Force; exit 0 }   # 두 번째 실패: 통과시키고 사용자에게 맡김
  New-Item $marker -ItemType File -Force | Out-Null
} else {
  if (Test-Path $marker) { Remove-Item $marker -Force }
}

$verify = Join-Path $root 'scripts\verify.ps1'
if (-not (Test-Path $verify)) { exit 0 }
$out = & powershell -NoProfile -ExecutionPolicy Bypass -File $verify -Quick 2>&1
if ($LASTEXITCODE -ne 0) {
  $tail = ($out | Select-Object -Last 40) -join "`n"
  [Console]::Error.WriteLine("scripts/verify.ps1 -Quick 실패. 작업을 끝내기 전에 아래 오류를 고치고 다시 검증하세요. 고칠 수 없으면 원인을 STATUS.md '막힌 항목'에 적고 사용자에게 보고하세요.`n$tail")
  exit 2
}
if (Test-Path $marker) { Remove-Item $marker -Force }
exit 0
