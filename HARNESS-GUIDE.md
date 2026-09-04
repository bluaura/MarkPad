# MarkPad 에이전트 하네스 적용 가이드

| 항목 | 내용 |
|---|---|
| 대상 | 이미 M0 + M1/E1(T-01~T-21)을 Claude(Fable 5)로 커밋한 MarkPad 저장소 |
| 목적 | 남은 M1(E2~E6)·M2 작업을 **같은 품질·같은 절차**로 반복 수행하게 하는 장치 설치 |
| 소요 | 설치 30분 + 첫 적용(상태 재구성) 1세션 |

## 1. 하네스가 무엇을 잡아주는가

중간에 하네스를 넣는 이유는 세 가지 누수를 막기 위해서다.

| 누수 | 증상 | 하네스 장치 |
|---|---|---|
| **컨텍스트 누수** — 세션마다 프로젝트를 다시 설명해야 함, 이전 세션이 알아낸 사실(부록 A 검증 결과 등)이 사라짐 | 같은 질문 반복, 이미 확인한 API를 다시 추측 | `CLAUDE.md` + `.claude/rules/` + `docs/STATUS.md` + SessionStart 훅 |
| **절차 누수** — 계획 없이 바로 코드, 테스트 없이 "완료", 문서와 코드 어긋남 | 브릿지 메서드가 C#에만 있고 TS엔 없음, DoD 미확인 완료 | `/task` 스킬(고정 절차) + `verifier` 서브에이전트 + `check-bridge-sync.ps1` |
| **품질 게이트 누수** — 빌드 깨진 채 세션 종료, 위험 명령 실행 | 다음 세션이 깨진 트리에서 시작 | Stop 훅(`verify.ps1 -Quick` 실패 시 종료 차단) + PreToolUse 훅(위험 명령 차단) + `permissions.deny` |

원칙은 하나다. **사람이 매번 프롬프트에 써 넣던 것을 파일로 옮긴다.** 그러면 프롬프트는 `/task T-28` 한 줄로 줄고, 나머지는 하네스가 강제한다.

## 2. 구성 요소

```
MarkPad/
├─ CLAUDE.md                        # 프로젝트 규칙 (≤200줄). 모든 세션에 자동 로드
├─ .claude/
│  ├─ settings.json                 # 권한 allow/deny + 훅 4종
│  ├─ hooks/
│  │  ├─ session-start.ps1          # 세션 시작: git log + STATUS.md 주입
│  │  ├─ guard-bash.ps1             # Bash 실행 전: force push·reset --hard·코퍼스 삭제 차단
│  │  ├─ post-edit.ps1              # 편집 후: TS 타입검사, WASDK 1.x 관용구·Core 계층 위반 감지, 브릿지 동기화 상기
│  │  └─ stop-verify.ps1            # 종료 전: 코드 변경 시 verify -Quick, 실패면 계속 작업시킴
│  ├─ rules/                        # 경로별 규칙 (해당 파일을 만질 때만 로드)
│  │  ├─ csharp.md  editor-web.md  docs.md
│  ├─ skills/
│  │  ├─ task/SKILL.md              # /task T-xx — 계획→구현→검증→STATUS→커밋
│  │  ├─ verify/SKILL.md            # /verify — 검증 실행·실패 분류
│  │  └─ status/SKILL.md            # /status — STATUS.md 재생성 (첫 적용 시 사용)
│  └─ agents/
│     └─ verifier.md                # 읽기 전용 DoD 검토 서브에이전트
├─ scripts/
│  ├─ verify.ps1                    # 사람·에이전트·CI 공용 검증 (-Quick 옵션)
│  └─ check-bridge-sync.ps1         # TS 핸들러 ↔ C# Bridge ↔ ARCHITECTURE §4 이름 대조
├─ docs/
│  ├─ STATUS.md                     # 살아있는 상태판 (템플릿: STATUS.template.md)
│  └─ ADR/                          # 결정·검증 기록
└─ .github/pull_request_template.md
```

역할 분담을 외워두면 조정하기 쉽다: **CLAUDE.md는 "항상 알아야 할 것"**, **rules는 "그 파일을 만질 때 알아야 할 것"**, **STATUS.md는 "지금 어디까지 왔는지"**, **스킬은 "절차"**, **훅은 "강제"**, **서브에이전트는 "다른 눈"**.

## 3. 설치 순서

### 3.1 파일 복사 (5분)
1. 이 폴더의 내용을 저장소 루트에 복사한다. 이미 `CLAUDE.md`가 있으면 병합(기존 내용 중 살릴 것만 "## 프로젝트 구조" 아래로).
2. `docs/STATUS.template.md`를 복사해 `docs/STATUS.md`를 만든다 (내용은 3.3에서 채움).
3. `docs/ADR/` 폴더가 없으면 만들고, M0에서 검증한 내용이 있으면 `ADR-011-m0-findings.md`로 옮긴다.
4. `.gitignore`에 `.claude/.stop-retry`, `.claude/settings.local.json` 추가.

### 3.2 환경 맞추기 (10분)
- 훅은 PowerShell(`powershell.exe`)로 실행된다. PowerShell 7을 쓰면 `settings.json`의 `powershell`을 `pwsh`로 바꿔도 된다. Claude Code는 Windows에서 훅 명령을 Git Bash로 실행하므로 경로에 `$CLAUDE_PROJECT_DIR`와 `/`를 쓴다(이미 그렇게 되어 있음).
- `scripts/verify.ps1`를 **손으로 먼저 한 번** 실행해 통과시킨다. 하네스 첫 적용 시 기존 코드가 검증을 못 넘으면 Stop 훅이 계속 세션을 붙잡는다. 통과가 안 되면 통과할 때까지는 `settings.json`의 `Stop` 블록을 잠시 지우고 진행한다.
- `check-bridge-sync.ps1`의 정규식은 현재 코드 관례(`bridge.register('doc.load')`, C# 상수/속성 문자열)에 맞는지 확인한다. 관례가 다르면 스크립트 상단 주석대로 정규식만 수정한다.
- `dotnet format`이 처음이면 `dotnet format MarkPad.sln`을 한 번 돌려 기준을 만들고 커밋한다(그 뒤로는 `--verify-no-changes`).
- `.editorconfig`가 없으면 추가한다(없으면 `dotnet format`이 의미가 없다).

### 3.3 첫 세션: 상태 재구성 (1세션)
이미 커밋된 T-01~T-21은 하네스 없이 만들어졌으므로 **"완료"라고 믿지 말고 재검증**한다.

```
claude
> /status
```
`/status` 스킬이 git log·파일 트리·검증 결과로 `docs/STATUS.md`를 채우고, DoD가 확인되지 않은 "완료" 작업과 ARCHITECTURE 부록 A 중 이미 답이 나온 항목을 보고한다. 이 보고를 보고:
1. DoD 미확인 작업은 `/task T-xx`로 다시 열어 DoD만 확인·보완한다(코드 재작성 아님). 특히 T-08/T-34 round-trip 코퍼스, T-10 IME, T-11 성능 기준선은 숫자가 STATUS.md에 있어야 한다.
2. 부록 A 확인 결과를 ARCHITECTURE.md에 "확인됨/수정됨(→ADR)"로 표기한다. **여기서 문서를 코드에 맞게 고치는 것이 하네스의 첫 실질 효과다** — 이후 세션이 문서를 믿고 일할 수 있게 된다.
3. `git commit -m "chore(build): 에이전트 하네스 도입 + STATUS 재구성"`.

### 3.4 이후 세션의 표준 루프
```
claude                      # SessionStart 훅이 STATUS.md와 최근 커밋을 보여줌
> /task T-28                # 계획 제시 → 확인 → 구현 → verify -Quick → verifier 검토 → STATUS 갱신 → 커밋
> /verify                   # 필요 시 전체 검증
```
세션 하나 = 작업 하나(T-xx) = 브랜치 하나. 규모 L(T-34, T-49)은 하위 ID(T-34a/b)로 쪼개 세션도 나눈다.

## 4. 운용 규칙 (사람 쪽)

1. **프롬프트에 절차를 쓰지 않는다.** 절차가 부족하면 스킬을 고친다. "테스트도 돌려줘"를 매번 쓰고 있다면 그건 하네스 결함이다.
2. **CLAUDE.md는 200줄 이하 유지.** 넘치면 `rules/`로 내린다. 한 번 어긴 규칙을 CLAUDE.md에 추가할 때는 "왜"를 한 줄 붙인다 — 이유 없는 규칙은 무시된다.
3. **STATUS.md는 에이전트가 쓰고 사람이 읽는다.** 사람이 직접 고치는 건 "다음 작업" 순서와 "결정 필요" 답변뿐.
4. **verifier는 M/L 작업에만.** S 작업까지 붙이면 토큰만 쓴다.
5. **훅이 시끄러우면 줄인다.** post-edit의 tsc가 느리면(대형 프로젝트) 해당 블록을 제거하고 Stop 훅에만 맡긴다. 훅은 "빠르고 확실한 것"만 남긴다.
6. **병렬 작업은 worktree.** 의존성이 없는 두 작업(예: T-22 렌더 완성과 T-18 MRU)은 `claude --worktree`로 각각 띄운다. `.worktreeinclude`에 `src/MarkPad.Editor.Web/node_modules`는 넣지 말고(용량) 각 worktree에서 `npm ci`를 돌리게 둔다.
7. **컨텍스트가 길어지면 `/compact`가 아니라 새 세션.** STATUS.md가 있으므로 새 세션의 손실이 거의 없다.

## 5. 이 하네스가 특히 지켜야 할 MarkPad 고유 위험

| 위험 | 장치 |
|---|---|
| Round-trip 코퍼스를 "고쳐서" 테스트 통과 | `permissions.deny`로 `corpus/roundtrip/**` 편집 차단 + `/verify` 스킬 규칙 |
| 빌드 산출물(`Assets/editor/`) 직접 수정 | deny + post-edit 경고 |
| WASDK 1.x 관용구(`Window.Current`) 혼입 | post-edit 정적 검사 |
| Core가 UI 네임스페이스 참조 | post-edit 정적 검사 + rules/csharp.md |
| 브릿지 3곳 불일치 | post-edit 상기 + `check-bridge-sync.ps1`(전체 verify에 포함) |
| 확인 안 된 Milkdown API 추측 | CLAUDE.md 작업 방식 6 + `/task` 2단계 규칙 ("`.d.ts`를 읽어라") |
| 한글 IME 회귀 | `/task` 3-3, verifier 6 — 자동화 불가라 **절차로 강제**(수동 확인 안내를 반드시 출력) |

## 6. 확장 (필요해질 때)

- **CI 동일화**: `.github/workflows/ci.yml`에서 `pwsh scripts/verify.ps1`만 호출. 로컬·에이전트·CI가 같은 스크립트를 쓰는 것이 핵심.
- **UI 스모크(T-59)** 가 생기면 `verify.ps1`에 `-Full` 스위치로 추가하고 Stop 훅에는 넣지 않는다(느림).
- **성능 회귀**: T-43 이후 `scripts/perf.ps1`(콜드 스타트·로드 시간 측정)을 만들고 `/verify full`에서만 실행, 결과를 `docs/perf/`에 append.
- **두 번째 서브에이전트**: 리뷰가 자주 같은 지적을 반복하면 그 지적을 rules로 옮기는 게 먼저다. 에이전트를 늘리는 건 마지막.

## 7. 점검 체크리스트 (설치 직후)

- [ ] `claude` 실행 시 세션 시작에 STATUS.md와 최근 커밋이 보인다
- [ ] `git push --force` 를 시켜보면 차단 메시지가 나온다
- [ ] `.cs` 파일에 `Window.Current`를 넣고 저장하면 경고가 컨텍스트에 뜬다
- [ ] 일부러 테스트를 깨고 세션을 끝내려 하면 Stop 훅이 막고, 두 번째 시도에서는 통과시킨다(무한 루프 방지)
- [ ] `/task T-22` 가 계획을 먼저 제시하고 멈춘다
- [ ] `/status` 결과의 완료 목록이 실제 커밋과 일치한다
