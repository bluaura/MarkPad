# SessionStart: 현재 상태와 최근 커밋을 컨텍스트로 주입한다.
# stdout은 Claude의 컨텍스트에 추가된다.
$ErrorActionPreference = 'SilentlyContinue'
$root = $env:CLAUDE_PROJECT_DIR
if (-not $root) { $root = (Get-Location).Path }

Write-Output "=== MarkPad 세션 컨텍스트 ==="
Write-Output "브랜치: $(git -C $root rev-parse --abbrev-ref HEAD)"
Write-Output "최근 커밋:"
git -C $root log --oneline -8
$dirty = git -C $root status --porcelain
if ($dirty) { Write-Output "작업 트리에 미커밋 변경 있음:"; $dirty | Select-Object -First 15 }

$status = Join-Path $root 'docs\STATUS.md'
if (Test-Path $status) {
  Write-Output ""
  Write-Output "=== docs/STATUS.md ==="
  Get-Content $status -TotalCount 80
}
Write-Output ""
Write-Output "규칙: 작업은 T-xx 단위. /task T-xx 로 시작. 끝낼 때 scripts/verify.ps1 통과 + STATUS.md 갱신."
exit 0
