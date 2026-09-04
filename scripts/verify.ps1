<#
.SYNOPSIS
  MarkPad 통합 검증. 사람과 에이전트가 같은 명령을 쓴다.
.PARAMETER Quick
  빠른 검증: .NET 빌드(웹 빌드 생략) + Core 테스트 + TS 타입검사 + vitest. (≈1~2분)
  기본(전체): 위 + 웹 번들 빌드 + eslint + dotnet format 검사 + 브릿지 동기화 검사.
.EXAMPLE
  pwsh scripts/verify.ps1 -Quick
#>
param([switch]$Quick)
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$failed = @()
function Step($name, [scriptblock]$body) {
  Write-Host "`n▶ $name" -ForegroundColor Cyan
  $sw = [Diagnostics.Stopwatch]::StartNew()
  & $body
  $code = $LASTEXITCODE
  $sw.Stop()
  if ($code -ne 0) { Write-Host "✖ $name 실패 ($($sw.Elapsed.TotalSeconds.ToString('0.0'))s)" -ForegroundColor Red; $script:failed += $name }
  else { Write-Host "✔ $name ($($sw.Elapsed.TotalSeconds.ToString('0.0'))s)" -ForegroundColor Green }
}

$web = Join-Path $root 'src/MarkPad.Editor.Web'
$hasWeb = Test-Path (Join-Path $web 'package.json')

if ($hasWeb -and -not (Test-Path (Join-Path $web 'node_modules'))) {
  Step 'npm ci' { Push-Location $web; npm ci --no-audit --no-fund; Pop-Location }
}

if ($hasWeb) {
  Step 'TypeScript 타입검사' { Push-Location $web; npx tsc --noEmit --pretty false; Pop-Location }
  Step 'vitest (round-trip 코퍼스 포함)' { Push-Location $web; npx vitest run --reporter=dot; Pop-Location }
  if (-not $Quick) {
    Step 'eslint' { Push-Location $web; npx eslint src --max-warnings 0; Pop-Location }
    Step '웹 번들 빌드' { Push-Location $web; npm run build; Pop-Location }
  }
}

# .NET: Quick 모드는 웹 빌드 타깃을 건너뛴다 (SkipEditorWeb)
$skip = if ($Quick) { '-p:SkipEditorWeb=true' } else { '' }
Step 'dotnet build' { dotnet build MarkPad.sln -c Debug --nologo -v minimal $skip -warnaserror:nullable }
Step 'dotnet test (Core)' { dotnet test tests/MarkPad.Core.Tests --no-build --nologo -v minimal }
if (-not $Quick) {
  Step 'dotnet format 검사' { dotnet format MarkPad.sln --verify-no-changes --no-restore }
  Step '브릿지 동기화 검사' { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-bridge-sync.ps1') }
}

Write-Host ""
if ($failed.Count) {
  Write-Host "검증 실패: $($failed -join ', ')" -ForegroundColor Red
  exit 1
}
Write-Host "검증 통과 $(if($Quick){'(Quick)'})" -ForegroundColor Green
exit 0
