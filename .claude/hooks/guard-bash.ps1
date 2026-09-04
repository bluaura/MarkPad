# PreToolUse(Bash): 위험 명령 차단. exit 2 + stderr 로 차단한다.
$ErrorActionPreference = 'SilentlyContinue'
$input_json = [Console]::In.ReadToEnd()
try { $payload = $input_json | ConvertFrom-Json } catch { exit 0 }
$cmd = [string]$payload.tool_input.command
if (-not $cmd) { exit 0 }

$patterns = @(
  'git\s+push\s+.*(--force|-f)\b',
  'git\s+reset\s+--hard',
  'git\s+branch\s+-D\b',
  'git\s+clean\b',
  'git\s+checkout\s+--\s+\.',
  'rm\s+-rf\s+/',
  'Remove-Item\s+.*-Recurse',
  'corpus[\\/]roundtrip.*(rm|del|Remove-Item|mv|Move-Item)',
  '(rm|del|Remove-Item|mv|Move-Item).*corpus[\\/]roundtrip'
)
foreach ($p in $patterns) {
  if ($cmd -match $p) {
    [Console]::Error.WriteLine("차단됨: '$cmd' 는 MarkPad 금지 명령 패턴($p)과 일치합니다. 사용자에게 직접 실행을 요청하세요.")
    exit 2
  }
}
exit 0
