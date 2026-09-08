# MarkPad 진행 상태

갱신: 2026-09-08 (탐색기 활성화 시 창 포그라운드 전환 수정) · 검증: `verify.ps1 -Quick` 통과 (tsc, vitest 163/163 + 1 skipped, dotnet build 경고 0, Core xUnit 50)

## 진행 중 (최대 1개)
- 없음. M0~M2 계획 항목(T-01~T-60) 전부 구현·커밋됨. 사용자 검증 대기.

## 다음 작업 (순서대로)
1. 사용자 검증: 한글 IME 체크리스트(`src/MarkPad.Editor.Web/test/ime-notes.md`) 수동 확인, 개인 md ~50개를 `corpus/roundtrip/personal-*.md`로 추가 후 `npm test -- roundtrip`
2. 실사용 2주 피드백 수집(설치본: `out/MarkPad.App_0.1.0.0_x64_Test/*.msix`, 2026-09-05 설치 확인)
3. M3-0 메모리 절감(`docs/perf/m1.md` 절감 후보 1·2): 뷰포트 밖 코드블록 정적 Lezer 하이라이트, KaTeX 지연 로드 — 100KB 문서 프라이빗 419MB → 목표 재정의(빈 문서 ≤300MB, 100KB ≤450MB)에 대한 PRD R3 결정 필요
4. M3 후보: T-61 블록 드래그(Crepe BlockEdit 재검토, ADR-02 예외), T-62 맞춤법(`spellcheck` 속성), T-63 DOCX 내보내기, T-64 이미지 크기 조절(`<img width>`), T-65 대용량 가상화

## 막힌 항목 / 결정 필요
- PRD §5.4 유휴 메모리 목표(≤150MB)는 WebView2 구조상 달성 불가 → 목표 재정의 승인 필요(`docs/perf/m1.md` 참조). PRD 변경은 사용자 승인 후 §9에 기록.
- 타이핑 지연(≤50ms)은 자동 측정 미구현 — 수동 DevTools Performance 측정 필요.
- 세션 데스크톱 잠금 환경에서는 키 입력 자동화가 제한됨 → FlaUI UI 테스트는 `MARKPAD_UI_TESTS=1` 게이트, 로컬에서만 실행.

## 완료
| 작업 | 날짜 | 커밋 | 비고 |
|---|---|---|---|
| T-01~T-08 M0 골격(솔루션·웹 번들·WebView2 호스트·브릿지 v0·Core IO·최소 툴바·round-trip v0) | 2026-09-04 | cde04d6 | |
| T-08/T-09/T-11/T-12 코퍼스 103/103, 성능 기준선, 부록 A 확인 | 2026-09-04 | f29d62a | ADR-011 |
| T-13~T-21 E1 셸(탭·IEditorSurface·단일 인스턴스·MRU·설정·상태바·단축키) | 2026-09-04 | 4e4d411 | |
| T-22~T-33, T-37~T-39 E2 편집 엔진 + E4 이미지 | 2026-09-04 | 9ffd10a | |
| T-34~T-36, T-40~T-44, T-47, T-56 round-trip v1·내보내기·복구·MSIX | 2026-09-04 | b219228 | |
| T-45~T-58 M2(개요·front matter 노드/GUI·설정·자동 저장·외부 변경·사이드바·블록 소스·서식 복사·읽기 전용) | 2026-09-04 | 70c5660 | |
| T-58b, T-59, T-60 이미지 속성·FlaUI 스모크·현지화/접근성 | 2026-09-04 | a6b6cf1 | |
| M2 잔여: 확대/축소 단축키, `==highlight==`, 제목 표시줄 메뉴, CI | 2026-09-04 | 5a29b20 | |
| 인라인 HTML 정제 렌더, T-43 성능 기록, T-54 기본 앱 안내, 정보 대화상자 | 2026-09-05 | fe9c0d2 | M0~M2 완료 |
| 원격 저장소 push(github.com/bluaura/MarkPad), CI 첫 실행 성공(run 33889893128) | 2026-09-05 | 4efefcc | 워크플로 YAML 콜론 수정 |
| 활성화 시 창 포그라운드 전환(콜드 스타트·두 번째 실행) | 2026-09-08 | (이 커밋) | `MainWindow.BringToFront()` + `AllowSetForegroundWindow` |

## M0 검증 결과 요약 (ADR-011 참조)
- Round-trip diff=0 비율: 103/103 (공개 README; 알려진 한계 1건은 `corpus/roundtrip-known-issues/`)
- IME: 자동화 불가 — `test/ime-notes.md` 체크리스트로 사용자 수동 확인 대기
- 성능 기준선(M2 시점, Release): 콜드 스타트→편집 가능 1.14~1.18s, 100KB 로드 0.35s, 1MB 로드 2.9s, 메모리 419MB(프라이빗, 100KB 문서, WebView2 트리 포함)
- ARCHITECTURE 부록 A: 7 항목 확인 완료(ADR-011), §4 브릿지 표·§5 canonical 규칙 갱신됨

## 알게 된 것 (다음 세션이 알아야 할 사실)
- WinUI 3 `Window.Activate()`만으로는 창이 앞으로 오지 않는다(실측: 실행 후에도 포그라운드가 다른 앱). `ShowWindow(SW_RESTORE)`(최소화 시) + `Activate()` + `SetForegroundWindow()`가 필요하다.
- 설치본(MSIX)에서 앱이 이미 실행 중일 때의 더블클릭은 두 번째 프로세스 없이 활성화만 전달되어 `AllowSetForegroundWindow`를 호출해 줄 쪽이 없다 → `SetForegroundWindow()`가 거부된다. 포그라운드 스레드에 `AttachThreadInput`으로 붙였다 떼는 폴백이 필요하며, 이 케이스는 개발 exe로는 재현되지 않고 **설치본으로만** 재현된다.
- GitHub Actions step 이름에 `: `가 들어가면 YAML 오류로 잡이 생성되지 않음 → 따옴표 필수. CI MSIX 아티팩트(168MB)는 `Dependencies/x64/Microsoft.WindowsAppRuntime.2.msix`(132MB)를 포함.
- 비ASCII가 든 `.ps1`은 반드시 UTF-8 **BOM** 포함으로 저장 — Windows PowerShell 5.1이 BOM 없는 UTF-8을 CP949로 읽어 파싱 오류(`verify.ps1`에서 발생, 2026-09-05 수정).
- CommunityToolkit.Mvvm 8.4 partial property `[ObservableProperty]`는 `LangVersion=preview` 필요.
- Visual Studio 없이 dotnet CLI만으로 WinUI 3 빌드·MSIX 패키징 가능(`-p:Packaged=true`, signtool은 NuGet BuildTools). 사이드로드 인증서 가져오기(`Import-Certificate` LocalMachine\TrustedPeople)는 관리자 권한 필요.
- 실행 별칭 `markpad.exe <file>`로 패키지 앱을 파일과 함께 실행 가능(설치 후 새 셸에서 PATH 반영).
- `AtomicWriter`의 File.Replace는 FileWatcher에 Renamed(tmp→target)로 도착 → Changed로 취급하고 발화 시점에 쓰기 시간을 다시 읽어 자기 저장 필터링.
- Crepe ImageBlock은 alt `1.00` 같은 숫자를 깨뜨림 → `plugins/image-alt` 우회. `remarkPreserveEmptyLinePlugin`은 round-trip을 깨서 제거.
- 메모리 급증 원인은 코드블록마다 생성되는 CodeMirror 인스턴스(1MB README에 수백 개) — M3 절감 1순위.
- 패키지 앱과 dev exe는 `%LOCALAPPDATA%\MarkPad`(settings.json, logs, recovery)를 공유.
