# MarkPad — 에이전트 작업 규칙

Windows용 WYSIWYG Markdown 에디터. WinUI 3(.NET 10, Windows App SDK 2.4) 셸 + WebView2 위의 Milkdown 7.21(Crepe) 편집기.

## 문서 (작업 전 반드시 참조)
- `docs/PRD-MarkPad.md` — 요구사항(F-xxx ID), 우선순위, 결정 기록
- `docs/ARCHITECTURE.md` — 프로젝트 구조, 인터페이스, 브릿지 프로토콜(§4), round-trip(§5), ADR
- `docs/IMPLEMENTATION-PLAN.md` — 작업 표(T-xx), DoD, 의존성
- `docs/STATUS.md` — **현재 진행 상태. 세션 시작 시 먼저 읽고, 세션 끝에 갱신한다.**
- `docs/ADR/` — 결정 기록. M0 검증 결과는 `ADR-011-m0-findings.md`

## 프로젝트 구조
- `src/MarkPad.App` — WinUI 3 앱 (Views, ViewModels, Bridge, Services, Controls)
- `src/MarkPad.Core` — UI 의존 없는 .NET 라이브러리 (Documents, Assets, Settings, Mru, Recovery, Export)
- `src/MarkPad.Editor.Web` — TypeScript/Vite 편집기 번들 → `MarkPad.App/Assets/editor/` (git 무시)
- `tests/MarkPad.Core.Tests` — xUnit / `src/MarkPad.Editor.Web/test` — vitest
- `corpus/roundtrip` — round-trip 검증 코퍼스 (diff=0 이어야 함)

## 명령
- 전체 검증: `pwsh scripts/verify.ps1` (빌드+테스트+lint). 빠른 검증: `pwsh scripts/verify.ps1 -Quick`
- .NET 빌드: `dotnet build MarkPad.sln -c Debug -p:SkipEditorWeb=true`
- .NET 테스트: `dotnet test tests/MarkPad.Core.Tests`
- 웹: `cd src/MarkPad.Editor.Web && npm run build` / `npm test` / `npm run lint` / `npm run typecheck`
- 앱 실행(WebView2에 Vite dev 서버 연결): `npm run dev` 후 `MARKPAD_EDITOR_URL=http://localhost:5173`로 F5
- 브릿지 동기화 검사: `pwsh scripts/check-bridge-sync.ps1`

## 작업 방식
1. 작업은 `docs/IMPLEMENTATION-PLAN.md`의 **T-xx 단위**로만 한다. `/task T-xx`로 시작한다.
2. 규모 M/L 작업은 코드를 고치기 전에 계획(건드릴 파일·순서·검증 방법)을 먼저 제시하고 확인을 받는다.
3. 구현 → `scripts/verify.ps1` 통과 → `docs/STATUS.md` 갱신 → 커밋. 검증 실패 상태로 작업을 끝내지 않는다.
4. 계획에 없는 파일·기능을 추가해야 하면 이유를 먼저 말하고 진행한다. 범위 확장(scope creep) 금지.
5. 문서와 코드가 다르면 **코드가 아니라 문서를 의심하고 사용자에게 알린다.** 임의로 설계를 바꾸지 않는다.
6. 확인되지 않은 라이브러리 API는 추측하지 말고 `node_modules` 타입 정의나 NuGet 패키지 소스를 읽어 확인한다.

## 코딩 규칙
- C#: nullable 활성, file-scoped namespace, DTO는 `record`, ViewModel은 CommunityToolkit.Mvvm `[ObservableProperty]`/`[RelayCommand]`, UI 스레드는 `DispatcherQueue.TryEnqueue`. Windows App SDK **2.x** 기준 — `Window.Current`, `DependencyObject.Dispatcher` 사용 금지.
- TS: `strict`, 브릿지 핸들러는 `src/handlers/*.ts`에 메서드당 하나, `bridge.register('doc.load', …)` 패턴.
- 브릿지 메서드를 추가·변경하면 같은 커밋에서 `Bridge/BridgeMessages.cs`, `src/bridge-types.ts`, `docs/ARCHITECTURE.md §4` 표를 모두 갱신한다.
- 설정 키를 추가하면 `AppSettings` 기본값 + 설정 화면 + `ARCHITECTURE.md §3.1` 스키마를 갱신한다.
- 테스트 없는 Core 로직 추가 금지. round-trip 변경은 코퍼스 전체 vitest 통과 필수.
- 커밋: Conventional Commits, scope `app|core|editor|build|docs`, 본문에 PRD ID(`F-EDIT-07`)와 작업 ID(`T-28`).

## 금지
- `git push --force`, `git reset --hard`, 브랜치 삭제, `corpus/` 파일 수정·삭제(추가는 허용)
- `Assets/editor/` 산출물 직접 편집 (원본은 `MarkPad.Editor.Web/src`)
- 문서 내용 삭제로 "정합성 맞추기" — 문서를 줄이지 말고 사용자에게 보고

## 상세 규칙
경로별 규칙은 `.claude/rules/`에 있다 (C#, 웹 에디터, 문서).
