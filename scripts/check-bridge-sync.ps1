<#
.SYNOPSIS
  브릿지 메서드 이름이 세 곳(TS 핸들러 등록, C# DTO/상수, ARCHITECTURE §4 표)에 모두 있는지 검사한다.
  누락이 있으면 목록을 출력하고 exit 1.
.NOTES
  인식 규칙 (프로젝트 관례에 맞게 정규식을 조정할 것):
   - TS : bridge.register('doc.load', …)        → 'doc.load'
   - C# : [BridgeMethod("doc.load")] 또는 const string DocLoad = "doc.load";
   - MD : ARCHITECTURE.md §4 표의 `doc.load` 백틱 항목 (table.* 처럼 와일드카드 항목은 접두사 매칭)
#>
$ErrorActionPreference = 'SilentlyContinue'
$root = Split-Path -Parent $PSScriptRoot
$rx = '[a-z]+\.[a-zA-Z]+'

$ts = Get-ChildItem (Join-Path $root 'src/MarkPad.Editor.Web/src') -Recurse -Include *.ts |
  Where-Object { $_.FullName -notmatch 'node_modules|\.spec\.|\.test\.' } |
  ForEach-Object { Select-String -Path $_.FullName -Pattern "register\(\s*['""]($rx)['""]" -AllMatches } |
  ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

$cs = Get-ChildItem (Join-Path $root 'src/MarkPad.App/Bridge') -Recurse -Include *.cs |
  ForEach-Object { Select-String -Path $_.FullName -Pattern "[""']($rx)[""']" -AllMatches } |
  ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique

$arch = Join-Path $root 'docs/ARCHITECTURE.md'
$mdText = Get-Content $arch -Raw
$md = [regex]::Matches($mdText, "``($rx|[a-z]+\.\*)``") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$mdPrefixes = $md | Where-Object { $_ -like '*.*' } | ForEach-Object { $_ -replace '\.\*$', '.' }

function InMd($name) { ($md -contains $name) -or ($mdPrefixes | Where-Object { $_.EndsWith('.') -and $name.StartsWith($_) }) }

$problems = @()
foreach ($m in $ts) {
  if (-not (InMd $m)) { $problems += "TS에 등록됨, ARCHITECTURE §4 표에 없음: $m" }
  if ($cs -and ($cs -notcontains $m)) { $problems += "TS에 등록됨, C# Bridge에 없음: $m" }
}
foreach ($m in $cs) {
  if ($m -match '^(doc|format|insert|table|find|outline|view|block|export|history|edit)\.' -and ($ts -notcontains $m)) {
    $problems += "C# Bridge에 있음, TS에 등록 안 됨: $m"
  }
}

if ($problems.Count) {
  Write-Host "브릿지 동기화 불일치 $($problems.Count)건:" -ForegroundColor Red
  $problems | ForEach-Object { Write-Host "  - $_" }
  exit 1
}
Write-Host "브릿지 동기화 OK (TS $($ts.Count)개, C# $($cs.Count)개, 문서 $($md.Count)개)" -ForegroundColor Green
exit 0
