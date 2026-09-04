# ADR-011: M0 스파이크 검증 결과 (ARCHITECTURE 부록 A 확정)

| 항목 | 내용 |
|---|---|
| 작성일 | 2026-09-04 |
| 상태 | 확정 |
| 관련 | IMPLEMENTATION-PLAN T-12, ARCHITECTURE.md 부록 A, PRD §6 Go/No-Go |

## 1. 환경 차이 (문서 대비)

| 항목 | 문서 | 실제 | 조치 |
|---|---|---|---|
| .NET SDK | 10.0 | 미설치(9.0.317) → winget으로 **10.0.400** 설치 | 완료 |
| Visual Studio | 2026/2022 | **없음** — `dotnet` CLI만으로 WinUI 3 빌드·실행 (WASDK NuGet의 XAML 컴파일러 사용) | `build/run-dev.ps1`로 실행·스크린샷 |
| Windows App SDK | 2.4.x | **2.4.0** (meta 패키지; WinUI 2.3.6 / Foundation 2.3.9 / Runtime 2.4.0). 개발 빌드는 언패키지드 + `WindowsAppSDKSelfContained=true` | gramPaper 프로젝트와 같은 패턴 |
| Milkdown | 7.21.x | **7.22.1** (`@milkdown/kit`, `@milkdown/crepe`) | 문서 갱신 |
| Vite / vitest / TS | 6 / – / 5.x | **7.3 / 3.2 / 5.9** (TypeScript 7.0은 Go 포팅판이라 보류) | 문서 갱신 |
| C# LangVersion | 14 | **preview** — CommunityToolkit.Mvvm 8.4의 partial property `[ObservableProperty]`가 요구 | Directory.Build.props |

## 2. 부록 A 가정 검증

| # | 가정 | 결과 | 상세 |
|---|---|---|---|
| 1 | 체크리스트 커맨드 export 이름 | **수정됨** | `preset-gfm`에 체크리스트 전용 커맨드 없음. `list_item.checked` 속성(`boolean|null`)을 `tr.setNodeMarkup`으로 토글 (`commands.ts setList('task')`). 입력 규칙 `wrapInTaskListInputRule`는 존재 |
| 2 | ImageBlock `onUpload(file) => Promise<string>` | **확인됨 + 추가** | `onUpload`/`inlineOnUpload`/`blockOnUpload` 3종, `proxyDomURL(url)`로 표시 URL 변환 가능 → ADR-05 이미지 해석기를 NodeView 없이 구현. **버그**: image-block은 md `alt` 슬롯에 종횡비(`ratio`)를 기록해 `![Big O graphs](x.png)`가 `![1.00](x.png)`로 저장됨 → `plugins/image-alt.ts`로 스키마 확장(alt 보존, ratio 미저장) |
| 3 | CodeMirror 언어 목록 설정 키 | **확인됨** | `CodeBlockConfig.languages: LanguageDescription[]`, `renderPreview(language, content, apply)` 훅으로 mermaid 미리보기를 feature 교체 없이 붙일 수 있음(T-47). 코드펜스 info string의 `meta`(` ```toml {file=…}`)는 Milkdown이 버림 → `plugins/code-meta.ts`로 보존 |
| 4 | `markdownUpdated` 매 트랜잭션 직렬화 여부 | **확인됨(직렬화함)** | `updated`만 구독하고 직렬화는 저장 시에만 수행. `changed` 150ms·`selection` 50ms 디바운스 |
| 5 | WinUI WebView2 환경 공유 | **확인됨** | `WebView2.EnsureCoreWebView2Async(CoreWebView2Environment)` 오버로드 존재. `CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options)`로 `%LOCALAPPDATA%\MarkPad\webview2` 지정 |
| 6 | 한글 IME | **수동 검증 대기** | 자동화 불가. `src/MarkPad.Editor.Web/test/ime-notes.md` 체크리스트로 사용자 검증 필요. 설계상 `isComposing` 중 단축키 가로채기 안 함 |
| 7 | WASDK 2.x FileSavePicker | **확인됨** | `Microsoft.Windows.Storage.Pickers.FileSavePicker(WindowId)` → `PickSaveFileAsync()` → `PickFileResult.Path`만 반환(파일 미생성). `AtomicWriter`가 생성 |
| 8 | (신규) 제목을 첫 자식으로 갖는 목록 항목 | **한계** | `list_item` content가 `paragraph block*`라 `- ### 제목`이 빈 항목 + 최상위 제목으로 분해됨. 코퍼스 1건 분리(`corpus/roundtrip-known-issues`). M2에서 스키마 확장 검토 |

## 3. Round-trip (T-08/T-09) — Go/No-Go

- 코퍼스: 공개 README/문서 100개 + 개인 문서 3개 (`corpus/roundtrip`, 라이선스는 `corpus/README.md`).
- 검증 방식: 실제 Crepe(jsdom)로 파싱·직렬화 → `roundtrip.ts` → 바이트 비교 (`test/milkdown-roundtrip.spec.ts`).
- 결과: **103/103 diff=0 (100%)**, 분리 1건 포함 시 103/104 = 99.0%. 기준(≥95%) 충족 → **Milkdown 확정, M1 진행**.
- 이를 위해 ARCH §5의 `canonical()`을 다음과 같이 구체화했다 (ARCHITECTURE §5 갱신):
  1. 인라인 콘텐츠를 ProseMirror식 **마크 런**(텍스트 + 정렬된 마크 집합)으로 평탄화 — `[_a_](u)` ≡ `_[a](u)_`, 마크 경계의 공백은 마크 밖으로.
  2. `linkReference`/`imageReference`는 문서의 `definition`으로 해석해 비교. `definition` 블록은 편집기가 만들 수 없으므로 **항상 원본 유지**.
  3. 인라인 HTML 정규화(`<br >` ≡ `<br />`), `<br />`만 있는 표 셀은 빈 셀.
  4. 표는 행별 후행 빈 셀 제거 후 최대 폭으로 `align` 정규화(GFM 비정형 표 ↔ 직사각형 ProseMirror 표).
  5. `list`/`listItem`의 `spread`, 텍스트·인라인 코드의 공백 차이는 무시.
- 엔진 측 보정: `remarkPreserveEmptyLinePlugin` 제거(빈 문단/셀이 `<br />`로 나오는 노이즈 제거), image alt 보존, code meta 보존.
- 미해결(엔진 한계): 항목 8. 목록 안의 한 항목만 고쳐도 목록 전체가 재직렬화되는 문제는 T-34(2단계 재귀 정렬)에서 다룬다.

## 4. 성능 기준선 (T-11)

`docs/perf/m0-baseline.md` 참조. 요약: 콜드 스타트→편집 가능 0.8~0.9초 ✅, 100KB 로드 0.56초 ✅, 1MB 로드 2.7초 ✅(UI 프리징은 미측정). **미달**: 유휴 메모리 — WebView2 프로세스 6개 포함 작업 집합 540MB / 프라이빗 269MB(목표 150MB), 설치 크기 153MB(self-contained 폴더 기준, 목표 60MB). 둘 다 T-43/T-44에서 다루며 메모리 목표는 프라이빗 바이트 기준으로 재정의를 제안한다.

## 5. 결정

1. **Milkdown/Crepe 확정** (TipTap 스파이크 불필요).
2. Round-trip은 ADR-03대로 AST 동등성 기반을 유지하되 §3의 canonical 규칙을 표준으로 삼는다.
3. 한글 IME(T-10)는 사용자 수동 검증 후 이 문서에 결과를 추가한다. 치명 이슈 발생 시 R2 대응(ProseMirror `handleDOMEvents` 조합 억제)을 우선 적용한다.
4. M1 진입 전 조건: T-10 결과 기록.
