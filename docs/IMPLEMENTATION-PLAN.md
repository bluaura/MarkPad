# MarkPad 구현 계획

| 항목 | 내용 |
|---|---|
| 문서 버전 | v1.0 |
| 작성일 | 2026-09-04 |
| 기준 문서 | PRD-MarkPad.md v0.3, ARCHITECTURE.md v1.0 |
| 상태 | 확정 — 이 문서의 작업 순서대로 구현 |

## 0. 사용 방법

- 작업 단위는 `T-xx`로 식별한다. 각 작업은 **산출물·완료 기준(DoD)·의존성·규모**를 갖는다. 규모: S(≤0.5일) / M(1~2일) / L(3~5일).
- 작업은 §2~§4의 순서를 기본으로 하되, 의존성이 없으면 병렬 진행 가능하다.
- 각 작업 완료 시 해당 PRD 요구사항 ID를 커밋 메시지에 적는다 (`feat(editor): F-EDIT-07 표 컨텍스트 바`).
- AI 코딩 에이전트에게 위임할 때는 작업 하나를 프롬프트 하나로 넘긴다. 작업 설명에 "건드릴 파일"과 "DoD"가 있으므로 그대로 사용한다.
- 마일스톤 종료 시 §6 검증 체크리스트를 수행하고 결과를 `docs/ADR/`과 PRD §9에 기록한다.

## 1. 전체 일정

| 마일스톤 | 기간(목표) | 작업 | 종료 조건 |
|---|---|---|---|
| **M0 스파이크** | 1~2주 | T-01 ~ T-12 | §6.1 M0 체크리스트 전부 통과. 엔진·구조 확정 |
| **M1 MVP (P0)** | 5~7주 | T-13 ~ T-44 | 본인 일상 문서 작업을 MarkPad로 전환. PRD G1~G5 수치 달성 |
| **M2 v1.0 (P1)** | 3~4주 | T-45 ~ T-60 | 2주간 다른 편집기 없이 사용 |
| **M3 (P2)** | 미정 | T-61 ~ | 필요 시 |

---

## 2. M0 — 스파이크 (기술 검증)

목적: PRD 리스크 R1(round-trip)·R2(한글 IME)·R3(성능)를 코드로 검증하고, ARCHITECTURE 부록 A의 가정을 확정한다. M0 코드는 버리지 않고 M1의 골격이 된다.

| ID | 작업 | 산출물 / 건드릴 파일 | DoD | 의존 | 규모 |
|---|---|---|---|---|---|
| T-01 | 저장소·솔루션 골격 | `MarkPad.sln`, `Directory.Build.props`, `Directory.Packages.props`, `src/MarkPad.App`(WinUI 3 템플릿, net10.0-windows, WASDK 2.4), `src/MarkPad.Core`, `tests/MarkPad.Core.Tests`, `.gitignore`, `.editorconfig` | `dotnet build` 성공, 빈 창 실행 | — | S |
| T-02 | 웹 에디터 프로젝트 골격 | `src/MarkPad.Editor.Web` — Vite+TS, `@milkdown/kit`·`@milkdown/crepe` 설치, `index.html`, `main.ts`에 Crepe 생성(Toolbar/TopBar/BlockEdit/AI off), light/dark CSS 변수 | `npm run dev`에서 브라우저로 md 편집 가능, `npm run build`가 `MarkPad.App/Assets/editor`에 출력 | — | S |
| T-03 | 빌드 파이프라인 연결 | `MarkPad.App.csproj` BeforeTargets `BuildEditorWeb`, `Assets/editor/**` Content 포함, `MARKPAD_EDITOR_URL` 개발 모드 분기 | VS에서 F5 한 번으로 웹 번들 빌드+앱 실행 | T-01, T-02 | S |
| T-04 | WebView2 호스트 + virtual host | `Views/EditorHost.xaml(.cs)`, `Services/WebViewEnvironment.cs`(공유 환경, 사용자 데이터 폴더), `app.markpad` 매핑, 내비게이션·새 창 차단, DevTools Debug만 | 앱 창 안에서 Crepe 편집기가 뜨고 타이핑 됨 | T-03 | M |
| T-05 | 브릿지 v0 | `Bridge/EditorBridge.cs`(req/res/evt, id, 타임아웃, ready 큐), `Bridge/BridgeMessages.cs`, `src/bridge.ts`, `bridge-types.ts`. 구현 메서드: `doc.load`, `doc.getMarkdown`, `format.toggle(bold)`, `format.heading`, evt `ready`·`changed` | C#에서 `doc.load` 후 굵게 버튼 → JS 반영, `changed` 수신해 창 제목에 ● 표시 | T-04 | M |
| T-06 | Core: 문서 열기/저장 최소 구현 | `Documents/Document.cs`, `DocumentIO.cs`, `EncodingDetector.cs`, `EolDetector.cs`, `AtomicWriter.cs` + xUnit (UTF-8/BOM/CP949/CRLF/LF/끝개행 픽스처 `corpus/fixtures/`) | 테스트 통과. 앱에서 Ctrl+O→편집→Ctrl+S 동작 (단, 아직 round-trip 없음) | T-01 | M |
| T-07 | 최소 툴바 | `Controls/FormatToolbar.xaml` — H1/H2/굵게/기울임/목록 5개 버튼, `ToolbarViewModel`, `selection` evt로 토글 상태 | 버튼 클릭·상태 동기화 확인 | T-05 | S |
| T-08 | Round-trip v0 | `src/roundtrip.ts` — unified 파서, canonical(), LCS 정렬, 조립 (ARCH §5). `doc.serializeForSave` 메서드. vitest `roundtrip.spec.ts` | `corpus/roundtrip/` 초기 30개 파일 무편집 diff=0. 편집 케이스 3개 스냅샷 | T-05 | L |
| T-09 | 코퍼스 수집 | `corpus/roundtrip/` — 공개 README 100개(다양한 스타일: 표·중첩 목록·HTML 인라인·front matter·수식 포함) + 본인 md 50개(민감 정보 제거) + 각 파일의 `.expected` 없음(원본=기대값) | 150개 파일, 라이선스 표기(`corpus/README.md`) | — | S |
| T-10 | 한글 IME 검증 | 수동 + `test/ime-notes.md` 기록. 조합 중 글자 깨짐, 조합 중 `changed` 이벤트 폭주, 두벌식/세벌식, 한자 변환, 표 셀·코드블록·링크 텍스트 안에서 조합 | §6.1 IME 항목 전부 통과. 이슈 있으면 재현 md + 회피책 기록 | T-05 | S |
| T-11 | 성능 기준선 측정 | `docs/perf/m0-baseline.md` — 콜드 스타트, 100KB/1MB 로드, 타이핑 지연(DevTools Performance), 유휴 메모리(탭 1·5개), 번들 크기 | PRD §5.4 표에 실측치 기입. 미달 항목은 M1 작업(T-43)에 반영 | T-05, T-06 | S |
| T-12 | 가정 검증 및 문서 갱신 | ARCH 부록 A 7개 항목 확인 결과를 `docs/ADR/ADR-011-m0-findings.md`에 기록, ARCHITECTURE·PRD 갱신 | 부록 A 항목별 "확인됨/수정됨" 표기 | T-05~T-11 | S |

**M0 Go/No-Go**: T-08 diff=0 비율 ≥ 95%(150개 중 143개) **그리고** T-10 IME 치명 이슈 0건이면 Milkdown 확정·M1 진행. 미달 시 PRD §6 규정대로 TipTap 스파이크 1주 추가.

---

## 3. M1 — MVP (PRD P0 전부)

### 3.1 에픽 E1: 파일·탭·셸
| ID | 작업 | 산출물 / 건드릴 파일 | DoD | 의존 | 규모 | PRD |
|---|---|---|---|---|---|---|
| T-13 | 셸 레이아웃 | `MainWindow.xaml` — `TitleBar`(WASDK 2.x), `TabView`, 툴바 슬롯, 콘텐츠, `StatusBar`. Mica 배경, 다크/라이트 자동 | PRD 4.1 와이어프레임과 일치, 창 크기·위치 복원 | T-12 | M | 4.1 |
| T-14 | 탭 관리 | `ShellViewModel`, `DocumentViewModel`, `Views/DocumentTab`. 새 탭·닫기(중간 클릭)·드래그 정렬·● 표시·닫기 확인 대화상자·Ctrl+Tab 순환·Ctrl+Alt+n | 탭 10개 열고 정렬·닫기·전환 오류 없음 | T-13 | M | F-FILE-03/05 |
| T-15 | IEditorSurface 추상화 + PlainTextHost | `Views/PlainTextHost.xaml`, `PlainTextSurface.cs`, `WebEditorSurface.cs`(T-05 리팩터). `.txt`는 TextBox, 툴바 비활성, TXT 배지 | `.txt` 열기·편집·저장, 툴바 회색 처리 | T-14 | M | F-FILE-11 |
| T-16 | 파일 열기 경로 통합 | `Services/ActivationService.cs` — 단일 인스턴스 리다이렉트, 파일 활성화, 명령줄, 드래그앤드롭(창 전체) | 탐색기에서 md 3개 선택→열기 시 한 창에 탭 3개 | T-14 | M | F-FILE-01 |
| T-17 | 새 문서·저장·다른 이름으로 | `FileSavePicker`(WASDK 2.x: 파일 미생성 → AtomicWriter가 생성), 기본 파일명 `Untitled.md`, 첫 H1이 있으면 제안 | Ctrl+N/S/Shift+S 전 흐름 | T-06, T-15 | S | F-FILE-02 |
| T-18 | 최근 파일 + 점프리스트 | `Core/Mru/RecentFilesStore.cs`(20개, 존재하지 않는 파일 표시 후 제거), 파일 메뉴·시작 화면(빈 상태 탭) 목록, `Services/JumpListService.cs` | 재시작 후 목록 유지, 작업표시줄 우클릭에 최근 항목 | T-16 | S | F-FILE-04 |
| T-19 | 인코딩 경고·변환 | 비UTF-8 감지 시 `InfoBar` "읽기 전용 — UTF-8로 변환하여 편집" 버튼 → 변환 후 편집 가능·저장 시 UTF-8 | CP949 픽스처로 확인 | T-06 | S | F-FILE-06 |
| T-20 | 상태바 | 단어·글자·인코딩·EOL·줄·저장 상태. `changed` evt 바인딩 | 값 실시간 갱신 | T-14 | S | 4.1 |
| T-21 | 앱 단축키 라우팅 | JS `hostKeymapPlugin`(Ctrl+S/N/O/W/Tab/Shift+Tab/P/,/Shift+E/Shift+T/Shift+O/Alt+n → `shortcut` evt), XAML `KeyboardAccelerator` 병행 | 포커스가 에디터 안/밖 모두에서 전 단축키 동작 (부록 B) | T-14 | S | 부록 B |

**E1 진행 상황 (2026-09-04)**: T-13~T-21 구현·빌드 완료(경고 0). 확인됨: 탭 3개 동시 열기(md·txt·cp949), 단일 인스턴스 리다이렉트(두 번째 프로세스가 종료되고 첫 창에 탭 추가), 읽기 전용 InfoBar+변환 버튼, 창 위치 저장/복원, MRU·시작 화면. **미확인**: 키 입력 기반 흐름(타이핑→Ctrl+S, Ctrl+Tab 순환, 탭 드래그 정렬)은 세션 데스크톱 잠금으로 자동화 불가 → `build/smoke-save.ps1`로 수동 실행. 점프리스트는 MSIX 패키징(T-44) 후에만 동작. 드래그앤드롭은 탭 줄·툴바에서는 원본 열기, 편집기 위에서는 복사본 열기(ARCH §4.4 `files.dropped`).

### 3.2 에픽 E2: 렌더링·편집 엔진
| ID | 작업 | 산출물 / 건드릴 파일 | DoD | 의존 | 규모 | PRD |
|---|---|---|---|---|---|---|
| T-22 | GFM 렌더 완성 | Crepe 기본 + 각주(`remark-gfm` footnote → 노드/직렬화 확인), 자동 링크, 수평선, 중첩 목록 스타일. `styles/editor.css` 타이포그래피(본문 폭 800, 제목 스케일, 표 스타일) | PRD F-VIEW-01 요소 전부 렌더되는 `corpus/fixtures/gfm-all.md` 시각 확인 | T-12 | M | F-VIEW-01 |
| T-23 | 코드블록 | Crepe CodeMirror feature 언어 목록 30+ 설정, 언어 드롭다운(검색), JS 컨텍스트 바(복사·줄번호·삭제), 블록 내 Tab 들여쓰기 | 30개 언어 하이라이트 스팟 체크, 컨텍스트 바 동작 | T-22 | M | F-VIEW-02, F-EDIT-08 |
| T-24 | 이미지 표시 | `plugins/image-resolver` — 상대/절대/원격/data 분기, `doc.markpad` 탭별 재매핑, 드라이브 문자 매핑(`image.resolve` req), `allowRemote` placeholder | 4종 src 픽스처 표시 확인, 탭 전환 시 이미지 유지 | T-22 | M | F-VIEW-03 |
| T-25 | 링크 동작 | Ctrl+클릭 → `link.open` req → http(s)는 브라우저, `.md` 상대 경로는 새 탭, 그 외 파일은 셸 실행 확인 대화상자. 일반 클릭은 커서 | 3종 링크 확인 | T-16, T-22 | S | F-VIEW-04 |
| T-26 | 인라인 자동 변환·목록 동작 | Milkdown inputrules 확인·보완(`# `, `- `, `1. `, `- [ ] `, `> `, ```` ``` ````, `**x**`, `---`), Enter/빈 항목 Enter 탈출/Tab/Shift+Tab | 8개 규칙 + 목록 키 동작 vitest/수동 확인 | T-22 | S | F-EDIT-04/05 |
| T-27 | 실행 취소/다시 실행 | ProseMirror history 확인, 탭별 독립(에디터 인스턴스별이므로 자연 충족), `history.undo/redo` 메서드 + 메뉴·툴바 | 100회 undo 정상 | T-22 | S | F-EDIT-06 |
| T-28 | 표 편집 | Crepe Table feature 기반 + JS 컨텍스트 바(행/열 추가·삭제·정렬·헤더 토글·표 삭제), Tab 셀 이동, `table.*` 메서드, 툴바 표 삽입 그리드 피커(XAML Flyout) | F-EDIT-07 시나리오 전부 | T-22 | M | F-EDIT-07, 부록 A |
| T-29 | 체크리스트 | 체크박스 클릭 토글, 툴바 버튼, `- [ ] ` 자동 변환과 통합 (M0 확인 결과에 따라 listItem.checked 방식) | 토글 후 저장 시 `[x]`/`[ ]` 정확 | T-26 | S | F-EDIT-09 |
| T-30 | 링크 편집 팝오버 | Crepe LinkTooltip 커스텀 문구/스타일, 툴바 링크 버튼 → 선택 텍스트 있으면 URL만 입력, 없으면 텍스트+URL. `Ctrl+K` | 삽입·수정·제거 | T-22 | S | F-EDIT-10 |
| T-31 | 찾기/바꾸기 | `plugins/find`, XAML `FindBar`(Ctrl+F/H, 대소문자·전체 단어, 다음/이전/바꾸기/모두), `find.*` 메서드 | 코드블록 포함 검색, 바꾸기 후 undo 1회로 복원 | T-22 | M | F-EDIT-11 |
| T-32 | 서식 툴바 전체 | `FormatToolbar` 부록 A 전 항목(하이라이트는 설정 OFF 시 숨김), 오버플로, `Ctrl+Shift+T` 숨김, 툴팁·접근성 이름, `selection` evt 전체 필드 | 부록 A 버튼·단축키 100% | T-23~T-31 | M | G3, 부록 A |
| T-33 | 테마 연동 | `Services/ThemeService.cs`(시스템 테마 감지) → `view.setTheme`, 에디터 다크 CSS, 코드블록 테마 쌍 | 시스템 다크 전환 시 즉시 반영 | T-22 | S | F-VIEW-08(기본) |

**E2 진행 상황 (2026-09-04)**: T-22~T-33 구현·빌드 완료(경고 0), E4의 T-37/T-38/T-39(AssetService·pending 이동·이미지 버튼 복사/참조)도 함께 구현. 스크린샷 확인: GFM 픽스처(`corpus/fixtures/gfm-all.md`) 렌더, 표 정렬, 체크리스트, KaTeX 블록 수식, 코드블록 라이트/다크 테마(CSS 변수 기반 CodeMirror 테마, 줄 번호), 툴바 상태 동기화, 다크 모드. `build/smoke-save.ps1` 통과(타이핑→Ctrl+S, 다른 블록 바이트 동일). vitest: 입력 규칙 7종·체크리스트 직렬화·찾기/바꾸기(전체 단어 한글, 모두 바꾸기 undo 1회). **미확인/보류**: 코드블록 언어 30개 스팟 체크(Crepe 기본 128개 언어 지연 로드), 링크 팝오버·표 그리드 피커·컨텍스트 바는 빌드만 확인(수동 확인 필요), 인라인 HTML은 raw 텍스트로 표시(M2에서 렌더 검토), 표 헤더 토글은 GFM 구조상 N/A.

### 3.3 에픽 E3: Round-trip·저장 품질
| ID | 작업 | 산출물 / 건드릴 파일 | DoD | 의존 | 규모 | PRD |
|---|---|---|---|---|---|---|
| T-34 | Round-trip v1 | T-08 개선: `list`/`blockquote` 2단계 재귀 정렬, front matter 비교, 후행 공백 보존, `changedBlocks` 보고. 코퍼스 150개 전체 | 무편집 diff=0 100% (예외 파일은 사유 문서화 후 코퍼스 분리), 편집 케이스 10개 스냅샷 | T-08, T-09 | L | G4, §5.3 |
| T-35 | 저장 규칙 | `Core/Settings`의 save 섹션 적용: EOL preserve/LF/CRLF, 끝 개행, BOM 보존 | 픽스처 6종 저장 후 바이트 비교 | T-06, T-34 | S | F-FILE-06 |
| T-36 | 직렬화 스타일 설정 | Milkdown remark-stringify 옵션(`emphasis`, `bullet`, `listItemIndent`)을 settings에서 주입 | 새 블록만 설정 스타일, 기존 블록은 원본 유지 확인 | T-34 | S | F-SET-04 |

### 3.4 에픽 E4: 이미지
| ID | 작업 | 산출물 / 건드릴 파일 | DoD | 의존 | 규모 | PRD |
|---|---|---|---|---|---|---|
| T-37 | 이미지 붙여넣기 | Crepe ImageBlock `onUpload` → `asset.save` req, `Core/Assets/AssetService.cs`(이름 규칙·충돌), `assets/` 생성 | 스크린샷 Ctrl+V → `assets/{doc}-{ts}.png` + 상대 링크, 렌더 즉시 | T-24 | M | F-IMG-01 |
| T-38 | 이미지 삽입 버튼·드롭 | `asset.pick` req(FileOpenPicker), 복사/참조 선택 대화상자, 파일 드롭(WebView2 2.x 드래그 지원 확인, 실패 시 XAML `AllowDrop`에서 처리) | 두 경로 모두 동작 | T-37 | S | F-IMG-02/03 |
| T-39 | 미저장 문서 이미지 | pending 폴더 저장 → 첫 저장 시 `RelocatePendingAsync` + `doc.rewriteAssetPaths` | 새 문서에 이미지 2장 → 저장 → `assets/`로 이동·링크 재작성·렌더 유지 | T-37, T-17 | M | F-IMG-04 |

### 3.5 에픽 E5: 내보내기
| ID | 작업 | 산출물 / 건드릴 파일 | DoD | 의존 | 규모 | PRD |
|---|---|---|---|---|---|---|
| T-40 | HTML 내보내기 | `export/renderHtml.ts`(unified+rehype+Shiki+KaTeX, mermaid→SVG), `Core/Export/HtmlExportBuilder.cs`(단일 파일, CSS 인라인, base64 옵션), 옵션 대화상자 | 브라우저에서 편집 화면과 동일하게 보임, 이미지 임베드 옵션 동작 | T-22 | M | F-EXP-01 |
| T-41 | PDF 내보내기 | `styles/print.css`, `export.preparePrint/restore`, `Services/PdfExportService.cs`(`PrintToPdfAsync`, A4/Letter·여백) | 20페이지 문서 PDF, 코드블록 페이지 넘김 깨짐 없음 | T-40 | M | F-EXP-02 |

### 3.6 에픽 E6: 안정성·성능·패키징
| ID | 작업 | 산출물 / 건드릴 파일 | DoD | 의존 | 규모 | PRD |
|---|---|---|---|---|---|---|
| T-42 | 오류 처리·복구 스냅샷 | `BridgeException`, `InfoBar` 표준화, WebView2 `ProcessFailed` 복구, `Core/Recovery/RecoveryStore.cs`(5초 스냅샷), 시작 시 복구 대화상자, 전역 예외 핸들러 | 프로세스 강제 종료 후 재시작 시 복구 제안 | T-14 | M | §5.5 |
| T-43 | 성능 최적화 | T-11 미달 항목 해결: WebView2 워밍업, 번들 코드 분할(Mermaid 동적), `changed` 페이로드 최소화, 1MB 경고 | PRD §5.4 전 항목 달성, `docs/perf/m1.md` 기록 | T-11, T-32 | M | G1/G2/G5 |
| T-44 | MSIX 패키징·설치 | `Package.appxmanifest`(파일 연결 `.md/.markdown`, 아이콘, 점프리스트), `build/make-cert.ps1`, `build/pack.ps1`, README 설치 절차 | 다른 PC에 사이드로드 설치 → 더블클릭으로 md 열림 | T-16, T-18 | M | F-FILE-10(부분) |

**M1 종료 조건**: §6.2 체크리스트 통과 + PRD G1~G5 실측 달성 + 본인 2주 사용 시작.

---

## 4. M2 — v1.0 (PRD P1)

| ID | 작업 | 산출물 / 건드릴 파일 | DoD | 의존 | 규모 | PRD |
|---|---|---|---|---|---|---|
| T-45 | 개요 패널 | `plugins/outline`, `Views/OutlinePane`, `Ctrl+Shift+O`, 현재 위치 강조 | 제목 클릭 이동, 편집 시 실시간 갱신 | T-32 | S | F-VIEW-05 |
| T-46 | 수식 | Crepe Latex feature 활성·툴바 버튼·설정 토글 | 인라인/블록 수식 렌더·편집 | T-32 | S | F-VIEW-06 |
| T-47 | Mermaid | `plugins/mermaid` NodeView(동적 import, 디바운스, 오류 표시) | flowchart·sequence 렌더, 오류 시 문자열 | T-23 | M | F-VIEW-06 |
| T-48 | Front matter 표시 | `plugins/frontmatter` 노드·스키마·직렬화·접힘 헤더(상위 3키 요약) | front matter 있는 문서 round-trip diff=0 유지 | T-34 | M | F-VIEW-07 |
| T-49 | Front matter GUI 편집기 | 트리 NodeView(`yaml` Document API, 중첩 map/seq, 타입별 컨트롤, 키 추가·삭제·이름변경·순서), raw 폴백+경고. vitest `frontmatter.spec.ts`(주석·순서 보존) | 중첩 3단계 YAML 편집 후 주석·순서·인용 스타일 보존 | T-48 | L | F-EDIT-15 |
| T-50 | 설정 화면 | `Dialogs/SettingsPage`(테마·폰트·폭·이미지·저장·직렬화·확장 토글), `Core/Settings/SettingsStore.cs` 검증·마이그레이션 | 모든 설정 즉시 반영, `settings.json` 수동 편집 반영 | T-33, T-36 | M | F-SET-01~05 |
| T-51 | 자동 저장 | 설정 간격 타이머, 저장 중 표시, 실패 시 InfoBar | 30초 간격 저장, 타이핑 중 커서 유지 | T-42 | S | F-FILE-07 |
| T-52 | 외부 변경 감지 | `DocumentIO.Watch`, dirty 여부에 따른 자동 재로드/InfoBar | 외부 편집기로 수정 시 5초 내 반영 | T-42 | S | F-FILE-08 |
| T-53 | 파일 탐색기 사이드바 | `Views/FileSidebar`(현재 폴더 트리, md 필터, 더블클릭 열기), `Ctrl+Shift+B` | 폴더 100개 파일 트리 지연 없음 | T-16 | M | F-FILE-09 |
| T-54 | 파일 연결 마무리 | 설치 시 기본 앱 안내, `.markdown` 포함, `.txt`는 "연결 프로그램"에만 | 설정 앱에서 MarkPad 선택 가능 | T-44 | S | F-FILE-10 |
| T-55 | 블록 소스 보기 | 우클릭 컨텍스트 메뉴 → 블록의 md 소스를 인라인 textarea로 편집 → 적용 시 재파싱 | 표·중첩 목록 소스 편집 후 정상 반영 | T-34 | M | F-EDIT-12 |
| T-56 | 인쇄 | `Ctrl+P` → PDF 파이프라인 재사용 → 시스템 인쇄 대화상자 | 프린터/PDF 프린터 출력 | T-41 | S | F-EXP-03 |
| T-57 | 서식 복사 | 선택 영역 → HTML+plain 클립보드 | Word·메일에 붙여넣기 서식 유지 | T-40 | S | F-EXP-04 |
| T-58 | 읽기 전용 토글 | 보기 메뉴 토글, `doc.setReadonly` | 편집 차단, 툴바 비활성 | T-32 | S | F-VIEW-09(P2→선택) |
| T-58b | 이미지 속성 편집 | 이미지 클릭 시 팝오버(alt·경로 편집, 삭제), 경로 변경 시 image-resolver 재해석 | alt 수정 후 저장 결과 `![alt](path)` 정확, 삭제 시 파일은 유지 | T-37 | S | F-IMG-05 |
| T-59 | UI 스모크 테스트 | `tests/MarkPad.App.UiTests`(FlaUI): 열기·편집·저장·탭·내보내기 5 시나리오 | CI에서 통과 | T-44 | M | §8.3 |
| T-60 | 접근성·현지화 마무리 | 고대비, 키보드 포커스 순서, `Resources.resw` ko/en | Accessibility Insights 치명 이슈 0 | T-50 | S | 4.3 |

**M3 후보(P2)**: T-61 블록 드래그(Crepe BlockEdit 재활성 검토), T-62 맞춤법, T-63 DOCX, T-64 이미지 크기 조절, T-65 대용량 가상화.

---

## 5. 구현 규칙

### 5.1 코딩 규칙
- C#: `nullable enable`, `file-scoped namespace`, `record`로 DTO, `async` 접미사, ViewModel은 `partial` + `[ObservableProperty]`/`[RelayCommand]`. UI 스레드 접근은 `DispatcherQueue.TryEnqueue`.
- TS: `strict`, ESLint(`@typescript-eslint/recommended`), 브릿지 메서드는 `bridge.register('doc.load', handler)` 패턴으로 한 파일(`handlers/*.ts`)에 하나씩.
- 브릿지 메서드 추가 시 **C# DTO·TS 타입·ARCH §4 표**를 같은 커밋에서 갱신한다.
- 설정 키 추가 시 `AppSettings` + 기본값 + 설정 화면 + ARCH §3.1 스키마 갱신.
- 커밋: Conventional Commits, scope는 `app|core|editor|build|docs`.

### 5.2 브랜치·리뷰
- `main`(항상 빌드 가능) ← `feat/T-xx-slug` PR. PR 템플릿에 DoD 체크와 PRD ID.
- M0 동안은 `main` 직접 커밋 허용.

### 5.3 Definition of Done (모든 작업 공통)
1. DoD 항목 충족 + 해당 xUnit/vitest 통과.
2. 신규 브릿지·설정은 문서 동기화.
3. 한글 입력 경로가 관련되면 IME 수동 확인 1회.
4. `dotnet build -c Release` 경고 0 (신규 코드 기준).

---

## 6. 검증 체크리스트

### 6.1 M0 종료 (2026-09-04 진행 상황)
- [ ] 한글 IME: 조합 중 글자 깨짐 없음 / 조합 중 백스페이스 / 표 셀 안 / 코드블록 안 / 링크 텍스트 안 / 한자 변환 / 영문↔한글 전환 직후 타이핑 — **수동 검증 대기** (`src/MarkPad.Editor.Web/test/ime-notes.md`)
- [x] `changed` 이벤트가 조합 중 문자마다 발생하지 않음(또는 디바운스로 흡수) — 150ms 디바운스, 값 변화 없으면 미발송 (`editor.ts`)
- [x] Round-trip 코퍼스 diff=0 비율 ≥ 95% — **103/103 (100%)**, 분리 1건 (`corpus/roundtrip-known-issues`)
- [x] 100KB 문서 로드 ≤ 1초 (0.56초) / 타이핑 지연은 IME 검증과 함께 수동 측정
- [x] 콜드 스타트 실측 기록(0.85초), WebView2 프로세스 포함 메모리 기록(540MB WS / 269MB private — 미달, T-43) — `docs/perf/m0-baseline.md`
- [x] ARCH 부록 A 7개 항목 확인 완료 — `docs/ADR/ADR-011-m0-findings.md`
- [x] Go/No-Go 결정 기록 — **Go (Milkdown 확정)**, IME 결과는 추후 추가

### 6.2 M1 종료 (PRD P0 매핑)
- [ ] F-FILE-01~06, 11 / F-VIEW-01~04 / F-EDIT-01~11 / F-IMG-01~04 / F-EXP-01~02 각 요구사항 수동 확인
- [ ] PRD 시나리오 S1~S5 실행
- [ ] 부록 A 툴바 버튼·단축키 전수, 부록 B 앱 단축키 전수(에디터 안·밖 포커스)
- [ ] G1 렌더 ≤1초 / G2 반영 ≤100ms / G3 툴바 100% / G4 diff=0 / G5 콜드 스타트 ≤1.5초·메모리 ≤150MB
- [ ] 강제 종료 후 복구, 저장 중 전원 차단 시뮬레이션(tmp 파일 잔존 시 원본 무손상)
- [ ] 사이드로드 설치 후 탐색기 더블클릭·다중 선택·점프리스트
- [ ] 다크/라이트 전환, 200% DPI

### 6.3 M2 종료
- [ ] P1 요구사항 전수 + front matter 중첩 YAML 주석 보존 테스트
- [ ] FlaUI 스모크 CI 통과
- [ ] 2주 실사용 후 이슈 목록 정리 → M3 범위 결정

---

## 7. 리스크 대응 계획 (PRD §7 매핑)

| PRD 리스크 | 검출 시점 | 대응 작업 |
|---|---|---|
| R1 round-trip | T-08 / T-34 | 95% 미달 시 canonical() 규칙 조정 → 그래도 미달이면 블록 단위를 더 세분(list item) |
| R2 한글 IME | T-10 | ProseMirror `handleDOMEvents.compositionstart/end`로 조합 중 트랜잭션 억제, 최후엔 TipTap 스파이크 |
| R3 메모리·시작 | T-11 / T-43 | WebView2 워밍업, 지연 로드, 번들 분할. 150MB 초과 시 목표를 200MB로 재협의(개인용) |
| R4 복잡 구조 UX | T-28 / T-55 | 컨텍스트 바 + 블록 소스 보기 |
| R5 대용량 | T-43 | 1MB 경고, M3 가상화 |
| R6 WinUI 3 버그 | T-13 / T-14 | WASDK 2.4 최신 패치 유지, TabView 이슈 시 커스텀 헤더 |

---

## 부록 A. 첫 주 작업 순서 (권장)
1일차 T-01, T-02, T-03 → 2일차 T-04 → 3~4일차 T-05, T-07 → 5일차 T-06 → 2주차 T-08(3일), T-09, T-10, T-11 병행 → T-12.

## 부록 B. 에이전트 위임용 프롬프트 템플릿
```
저장소: MarkPad (docs/ARCHITECTURE.md, docs/IMPLEMENTATION-PLAN.md 참조)
작업: T-xx <제목>
건드릴 파일: <표의 산출물 열>
완료 기준: <표의 DoD 열>
제약: ARCHITECTURE.md §<관련 절>의 인터페이스·프로토콜을 따를 것. 브릿지 메서드를 추가하면 C# DTO, TS 타입, ARCHITECTURE §4 표를 함께 갱신할 것. 테스트를 추가하고 통과시킬 것.
결과 보고: 변경 파일 목록, 테스트 결과, 문서에서 갱신이 필요한 부분.
```
