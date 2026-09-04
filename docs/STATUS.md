# MarkPad 진행 상태

갱신: YYYY-MM-DD (커밋 abc1234) · 검증: `verify.ps1 -Quick` 통과/실패

## 진행 중 (최대 1개)
- T-xx <제목> — 브랜치 `feat/T-xx-...` — 현재 단계: 계획/구현/검증 — 남은 것: …

## 다음 작업 (순서대로)
1. T-xx <제목> (의존: T-yy 완료)
2. …

## 막힌 항목 / 결정 필요
- …

## 완료
| 작업 | 날짜 | 커밋 | 비고 |
|---|---|---|---|
| T-01 저장소·솔루션 골격 | 2026-09-xx | abc1234 | |

## M0 검증 결과 요약 (ADR-011 참조)
- Round-trip diff=0 비율: …/150
- IME: …
- 성능 기준선: 콜드 스타트 …s, 100KB 로드 …s, 메모리 …MB
- ARCHITECTURE 부록 A: 1 확인됨 / 2 수정됨(ADR-012) / 3 미확인 …

## 알게 된 것 (다음 세션이 알아야 할 사실)
- 예: Crepe ImageBlock의 onUpload는 `(file: File) => Promise<string>` 시그니처 — ARCH 부록 A-2 확인됨
- 예: WinUI 3 WebView2는 `EnsureCoreWebView2Async(env)` 오버로드 지원 — 부록 A-5 확인됨
