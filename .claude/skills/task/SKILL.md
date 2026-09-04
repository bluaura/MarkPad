---
name: task
description: IMPLEMENTATION-PLAN.md의 T-xx 작업 하나를 계획→구현→검증→STATUS 갱신→커밋 순서로 수행한다. 사용자가 "/task T-28", "T-28 진행해줘", "다음 작업 해줘"라고 하면 사용.
argument-hint: <T-xx | next>
---
# 작업 실행 절차

인자: `$ARGUMENTS` (예: `T-28`. `next`면 `docs/STATUS.md`의 "다음 작업" 첫 항목).

## 0. 준비
1. `docs/STATUS.md`를 읽는다. "진행 중" 작업이 이미 있고 이번 인자와 다르면 **멈추고 사용자에게 묻는다** (동시에 두 작업 금지).
2. `docs/IMPLEMENTATION-PLAN.md`에서 해당 T-xx 행을 찾아 **산출물·DoD·의존·규모·PRD ID**를 그대로 인용한다.
3. 의존 작업이 STATUS.md 완료 목록에 없으면 멈추고 보고한다.
4. PRD의 해당 F-xxx 행과 ARCHITECTURE의 관련 절(§3 컴포넌트, §4 브릿지, §5 round-trip 등)을 읽는다.
5. 브랜치: `git switch -c feat/T-xx-<slug>` (main에서). 이미 브랜치에 있으면 유지.

## 1. 계획 (규모 M/L는 필수, S는 3줄 요약)
- 건드릴 파일(신규/수정), 순서, 각 단계의 검증 방법, 예상 리스크를 제시하고 **확인을 받은 뒤** 코드를 고친다.
- 계획에서 ARCHITECTURE와 다르게 해야 하는 지점이 있으면 여기서 명시한다.

## 2. 구현
- 작은 단위로 편집하고, 단계마다 관련 테스트를 먼저 돌린다 (`dotnet test …`, `npx vitest run <file>`).
- 확인되지 않은 API는 `node_modules/@milkdown/*/lib/*.d.ts`, NuGet 패키지 XML 문서 등 원본을 읽어 확인한다. 추측 금지.
- 브릿지/설정 변경 시 CLAUDE.md의 동기화 규칙을 따른다.

## 3. 검증
1. `pwsh scripts/verify.ps1 -Quick` 통과.
2. DoD 항목을 하나씩 **실제로 확인**하고 결과를 표로 남긴다 (자동 검증 불가 항목은 "수동 확인 필요"로 표기하고 사용자가 확인할 절차를 적는다).
3. 한글 입력 경로가 관련되면 IME 확인 절차를 사용자에게 안내한다.
4. 규모 M/L는 `verifier` 서브에이전트에게 DoD 대조 검토를 맡기고 결과를 반영한다.

## 4. 마무리
1. `docs/STATUS.md` 갱신: 완료 목록에 `T-xx (날짜, 커밋 해시)` 추가, 진행 중 비우기, 다음 작업 갱신, 발견한 이슈는 "막힌 항목/알게 된 것"에.
2. ARCHITECTURE 부록 A 항목을 확인했다면 "확인됨/수정됨" 표기 + 필요 시 `docs/ADR/` 추가.
3. 커밋: `feat(scope): T-xx <제목> [F-xxx-nn]` — 본문에 DoD 확인 표 요약. 문서 변경은 같은 커밋.
4. 보고: 변경 파일, 테스트 결과, 수동 확인이 필요한 항목, 문서 갱신 여부를 간결히.
