# MarkPad 아키텍처 문서

| 항목 | 내용 |
|---|---|
| 문서 버전 | v1.0 |
| 작성일 | 2026-09-04 |
| 기준 문서 | PRD-MarkPad.md v0.3 |
| 관련 문서 | IMPLEMENTATION-PLAN.md v1.0 |
| 상태 | 확정 — 구현 착수 기준 |

본 문서는 PRD v0.3의 요구사항을 구현하기 위한 시스템 구조, 컴포넌트 책임, 인터페이스 계약, 핵심 알고리즘을 정의한다. 구현 순서와 작업 분해는 IMPLEMENTATION-PLAN.md에 있다.

---

## 1. 기술 스택 (버전 고정)

| 구성 요소 | 버전 | 비고 |
|---|---|---|
| .NET | **10.0 (LTS)** | TFM `net10.0-windows10.0.19041.0` |
| Windows App SDK | **2.4.x** (2026-08 안정판) | SemVer 체계. NuGet `Microsoft.WindowsAppSDK` 2.4.* |
| WinUI 3 | Windows App SDK 동봉 | `TabView`, `CommandBar`, `MicaBackdrop` 사용. WASDK 2.3.6 WinUI에는 `TitleBar` 컨트롤이 없어 `ExtendsContentIntoTitleBar` + `SetTitleBar(TabStripFooter)`로 탭을 제목 표시줄에 넣는다 |
| WebView2 | Evergreen 런타임 (OS 기본 탑재) | `Microsoft.Web.WebView2` — Windows App SDK가 참조하는 버전 사용. 최소 런타임 버전을 앱 시작 시 검사 |
| C# 언어 | C# 14 | nullable 활성, `ImplicitUsings` |
| MVVM | CommunityToolkit.Mvvm 8.x | `ObservableObject`, `RelayCommand`, Messenger |
| DI | Microsoft.Extensions.DependencyInjection | `App.Services` |
| 로깅 | Microsoft.Extensions.Logging + Serilog (파일 싱크) | `%LOCALAPPDATA%\MarkPad\logs\` |
| 편집 엔진 | **Milkdown 7.22.x** — `@milkdown/kit`, `@milkdown/crepe` (M0 기준 7.22.1) | Crepe 기반 + 커스텀 플러그인 |
| Markdown 파서 (round-trip) | unified / remark-parse / remark-gfm / remark-frontmatter / remark-math / mdast-util-* | Milkdown 내부와 동일 계열 |
| YAML | `yaml` (eemeli/yaml) 2.x | `parseDocument` — 주석·순서 보존 편집 |
| 코드 하이라이팅 | CodeMirror 6 (Crepe CodeMirror feature 내장) | 편집 중 하이라이팅. 내보내기 시 Shiki 정적 하이라이팅 |
| 수식 | KaTeX (Crepe Latex feature 내장) | 오프라인 번들 |
| 다이어그램 | Mermaid 11.x | 코드블록 lang=mermaid 미리보기 NodeView |
| 웹 빌드 | Vite 7 + TypeScript 5.9, vitest 3 (jsdom) | 단일 번들 → `MarkPad.App/Assets/editor/` (CodeMirror 언어·Mermaid는 지연 청크) |
| 패키징 | MSIX (Windows Application Packaging 프로젝트 통합, 자체 서명 인증서) | 사이드로드 |
| 테스트 | xUnit (.NET), vitest (web), FlaUI (UI 스모크, 선택) | |

> PRD §5.1의 ".NET 8"은 본 문서에서 **.NET 10 LTS**로 상향한다 (2025-11 출시, 2028-11까지 지원). Windows App SDK는 1.x가 아닌 2.x 계열을 사용하므로 `Window.Current`·`DependencyObject.Dispatcher` 등 1.x 관용구를 쓰지 않는다.

---

## 2. 시스템 구조

### 2.1 계층도
```
┌───────────────────────────── MarkPad.App (WinUI 3, C#) ─────────────────────────────┐
│  Presentation                                                                        │
│   MainWindow ── TitleBar / TabView / FormatToolbar / OutlinePane / StatusBar          │
│   DocumentTab ── EditorHost(WebView2)  |  PlainTextHost(TextBox)                     │
│   Dialogs ── Settings / ExportOptions / UnsavedChanges / EncodingWarning              │
│  ViewModels (CommunityToolkit.Mvvm)                                                  │
│   ShellViewModel · DocumentViewModel · ToolbarViewModel · OutlineViewModel · Settings │
│  Bridge                                                                              │
│   EditorBridge (C# ↔ JS JSON-RPC over WebView2 WebMessage)                           │
└──────────────────────────────────────┬───────────────────────────────────────────────┘
                                       │ 참조
┌──────────────────────────────────────▼──────── MarkPad.Core (netstandard-free .NET 10 classlib) ─┐
│  Document        : 문서 상태 (경로, 원본 텍스트, EOL, BOM, 인코딩, dirty)                            │
│  DocumentIO      : 열기(인코딩 감지) / 원자적 저장 / 외부 변경 감시                                    │
│  AssetService    : 이미지 저장 경로 규칙, 임시→정식 이동                                              │
│  ExportService   : HTML 단일 파일 조립 (PDF는 App 계층 WebView2 의존)                               │
│  RecentFiles     : MRU 저장·로드                                                                    │
│  Settings        : settings.json 모델·직렬화·검증                                                   │
│  Recovery        : 스냅샷 저장·복구 목록                                                             │
└─────────────────────────────────────────────────────────────────────────────────────────────────┘

┌────────────────────────── MarkPad.Editor.Web (TypeScript, Vite → 정적 번들) ──────────────────────┐
│  main.ts        : 부트스트랩, Crepe 생성, 테마 적용                                                │
│  bridge.ts      : JSON-RPC 서버 (host→web 명령 처리, web→host 이벤트 송신)                           │
│  commands.ts    : 툴바 명령 → Milkdown 커맨드 매핑                                                  │
│  roundtrip.ts   : AST 동등성 기반 원본 보존 직렬화 (§5)                                             │
│  plugins/frontmatter/  : YAML front matter 노드 + 트리 편집 NodeView                                │
│  plugins/image-resolver/ : 상대·절대·원격 이미지 src 해석                                            │
│  plugins/mermaid/       : mermaid 코드블록 미리보기                                                  │
│  plugins/find/          : 찾기/바꾸기 데코레이션                                                     │
│  plugins/outline/       : 제목 트리 추출·스크롤                                                     │
│  export/        : 정적 HTML 렌더 (Shiki, KaTeX, Mermaid SVG 인라인)                                 │
│  styles/        : 편집 테마(light/dark), print.css                                                 │
└─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 프로세스 구조
- MarkPad는 **단일 인스턴스** 앱이다. 두 번째 실행(탐색기 더블클릭 등)은 `AppInstance.FindOrRegisterForKey("main")`로 기존 인스턴스에 리다이렉트하고, 활성화 인자(파일 경로)를 넘겨 새 탭으로 연다.
- WebView2는 **앱 전체에서 하나의 `CoreWebView2Environment`**를 공유하고, 탭마다 `WebView2` 컨트롤을 둔다. 사용자 데이터 폴더는 `%LOCALAPPDATA%\MarkPad\webview2\`로 고정한다. 탭 수가 늘어도 브라우저 프로세스는 공유되므로 메모리 증가는 렌더러 프로세스 단위로만 발생한다.
- `.txt` 탭은 WebView2를 만들지 않는다 (`PlainTextHost`).

### 2.3 저장소 레이아웃
```
MarkPad/
├─ MarkPad.sln
├─ Directory.Build.props            # 공통 TFM·nullable·경고 설정
├─ Directory.Packages.props         # 중앙 패키지 버전 관리
├─ src/
│  ├─ MarkPad.App/                  # WinUI 3 앱 (MSIX 패키징 포함)
│  │  ├─ App.xaml(.cs)              # DI 컨테이너, 단일 인스턴스, 전역 예외
│  │  ├─ MainWindow.xaml(.cs)
│  │  ├─ Views/                     # ShellView, DocumentTab, EditorHost, PlainTextHost, OutlinePane, StatusBar
│  │  ├─ Controls/                  # FormatToolbar, TableContextBar, CodeBlockContextBar, FindBar
│  │  ├─ Dialogs/
│  │  ├─ ViewModels/
│  │  ├─ Bridge/                    # EditorBridge, BridgeMessages(DTO), BridgeException
│  │  ├─ Services/                  # PdfExportService(WebView2), JumpListService, ThemeService, ActivationService
│  │  ├─ Assets/
│  │  │  ├─ editor/                 # ← Vite 빌드 산출물 (git 무시, 빌드 시 생성)
│  │  │  └─ icons/
│  │  ├─ Package.appxmanifest       # 파일 연결(.md/.markdown), 점프리스트, 아이콘
│  │  └─ MarkPad.App.csproj         # PreBuild: `npm run build` 실행 (§8.2)
│  ├─ MarkPad.Core/
│  │  ├─ Documents/                 # Document, DocumentIO, EncodingDetector, EolDetector, AtomicWriter, FileWatcher
│  │  ├─ Assets/                    # AssetService, AssetNaming
│  │  ├─ Export/                    # HtmlExportBuilder
│  │  ├─ Settings/                  # AppSettings, SettingsStore
│  │  ├─ Mru/                       # RecentFilesStore
│  │  └─ Recovery/                  # RecoveryStore
│  └─ MarkPad.Editor.Web/
│     ├─ package.json  vite.config.ts  tsconfig.json
│     ├─ index.html
│     ├─ src/ (main.ts, bridge.ts, commands.ts, roundtrip.ts, plugins/, export/, styles/)
│     └─ test/ (vitest: roundtrip.spec.ts, frontmatter.spec.ts, commands.spec.ts)
├─ tests/
│  ├─ MarkPad.Core.Tests/           # xUnit
│  └─ MarkPad.App.UiTests/          # FlaUI 스모크 (M2)
├─ corpus/
│  ├─ roundtrip/                    # 원본 보존 검증용 .md (README 100 + 개인 50)
│  └─ fixtures/                     # 인코딩·EOL·front matter 케이스
├─ docs/                            # PRD, ARCHITECTURE, IMPLEMENTATION-PLAN, ADR/
└─ build/                           # 서명 인증서 생성 스크립트, MSIX 빌드 스크립트
```

---

## 3. 컴포넌트 설계

### 3.1 MarkPad.Core

#### Document
```csharp
public sealed class Document
{
    public string? Path { get; init; }             // null = 미저장 새 문서
    public DocumentKind Kind { get; init; }        // Markdown | PlainText
    public string OriginalText { get; private set; }   // 마지막 로드/저장 시점 텍스트 (LF 정규화)
    public Encoding Encoding { get; init; }        // UTF8(no BOM) 기본
    public bool HasBom { get; init; }
    public LineEnding Eol { get; init; }           // Lf | CrLf | Mixed(→ 첫 개행 기준)
    public bool EndsWithNewline { get; init; }
    public bool IsReadOnly { get; init; }          // 비UTF-8 감지 시 true
    public bool IsDirty { get; set; }
    public DateTimeOffset? LastWriteTimeOnLoad { get; init; }   // 외부 변경 감지 기준
}
```
- 텍스트는 Core 내부에서 항상 **LF로 정규화**해 보관하고, 저장 시 `Eol`로 되돌린다. JS 쪽은 LF만 본다.
- `OriginalText`는 round-trip의 기준값이다. 저장 성공 시 저장한 텍스트로 갱신한다.

#### DocumentIO
| 메서드 | 동작 |
|---|---|
| `Task<Document> OpenAsync(string path)` | 바이트 읽기 → `EncodingDetector` (BOM → UTF-8 strict 디코드 → 실패 시 CP949 시도 → 실패 시 Latin-1 폴백 + ReadOnly) → `EolDetector` → `Document` |
| `Task SaveAsync(Document doc, string textLf, string? newPath = null)` | LF→Eol 복원, BOM·끝 개행 정책 적용 → `AtomicWriter.Write` → `OriginalText`·`LastWriteTimeOnLoad` 갱신 |
| `IDisposable Watch(Document doc, Action<ExternalChange> onChange)` | `FileSystemWatcher` (Changed/Renamed/Deleted), 500ms 디바운스, 자기 저장은 `LastWriteTimeOnLoad`로 필터 |

`AtomicWriter`: 같은 디렉터리에 `.{name}.markpad-tmp` 작성 → `FileStream.Flush(true)` → `File.Replace(tmp, target, backup:null)`; 대상이 없으면 `File.Move`. 실패 시 tmp 삭제 후 예외.

#### AssetService
- `string ResolveAssetsDir(Document doc)` → `{문서 폴더}/{settings.imageFolderName}` (기본 `assets`). 미저장 문서는 `%LOCALAPPDATA%\MarkPad\pending-assets\{docId}\`.
- `Task<string> SaveImageAsync(Document doc, ReadOnlyMemory<byte> png, string? ext)` → 파일명 `{문서파일명}-{yyyyMMdd-HHmmss}[-{n}].{ext}` (충돌 시 `-n`), **상대 경로**(`assets/x.png`, 항상 `/` 구분자) 반환.
- `Task RelocatePendingAsync(Document doc, string newPath, IReadOnlyList<string> refs)` → 첫 저장 시 pending 폴더의 파일을 정식 `assets/`로 이동하고 (`old rel → new rel`) 매핑을 돌려줘 JS가 링크를 재작성하게 한다.

#### Settings
`%LOCALAPPDATA%\MarkPad\settings.json`, `System.Text.Json` source-generated. 알 수 없는 키는 보존(`JsonExtensionData`). 스키마:
```jsonc
{
  "schemaVersion": 1,
  "theme": "system",                 // light | dark | system
  "editor": { "fontFamily": "Segoe UI Variable", "fontSize": 15, "lineHeight": 1.7, "maxWidth": 800, "zoom": 1.0 },
  "images": { "folderName": "assets", "allowRemote": true },
  "save": { "eol": "preserve", "ensureTrailingNewline": true, "autosave": false, "autosaveIntervalSec": 30 },
  "markdown": { "emphasis": "*", "bullet": "-", "listIndent": 2, "extHighlight": false, "extMath": true, "extMermaid": true, "frontMatter": true },
  "ui": { "showToolbar": true, "showOutline": false, "showFileSidebar": false, "window": { "x":0,"y":0,"w":1200,"h":800,"maximized":false } }
}
```

### 3.2 MarkPad.App

#### 활성화·수명주기 (ActivationService)
1. `Program.Main` 대체: `App` 생성자 전에 `AppInstance.GetCurrent().GetActivatedEventArgs()` 확인 → `FindOrRegisterForKey("MarkPad.Main")` → 현재가 주 인스턴스가 아니면 `RedirectActivationToAsync` 후 종료.
2. 주 인스턴스: `Activated` 이벤트로 후속 파일 활성화 수신 → `ShellViewModel.OpenFilesAsync(paths)`.
3. 명령줄 인자(`MarkPad.exe a.md b.md`)·파일 연결(`FileActivatedEventArgs`)·드래그앤드롭 모두 같은 진입점.
4. 종료 시: 미저장 탭 확인 대화상자 → 창 위치 저장 → 복구 스냅샷 정리.

#### ShellViewModel
- `ObservableCollection<DocumentViewModel> Tabs`, `SelectedTab`
- 명령: New, Open, Save, SaveAs, CloseTab, CloseOthers, OpenRecent, Export(Html/Pdf), Print, ToggleOutline, ToggleToolbar, ToggleSidebar, Settings
- 활성 탭이 바뀌면 `ToolbarViewModel.Attach(tab)`으로 툴바 상태 소스를 교체한다.

#### DocumentViewModel
- `Document`(Core) + `IEditorSurface`(§3.3) + `OutlineViewModel` + `FindViewModel`
- 상태: `Title`, `IsDirty`, `WordCount`, `CharCount`, `EncodingLabel`, `EolLabel`, `CursorLine`, `IsReadOnly`, `Kind`
- 흐름: `LoadAsync` → surface.Load(textLf) → 이벤트 구독. `SaveAsync` → surface.SerializeForSave(originalLf) → DocumentIO.SaveAsync.

#### IEditorSurface (탭 표면 추상화)
```csharp
public interface IEditorSurface : IAsyncDisposable
{
    Task LoadAsync(string textLf, EditorLoadOptions o);
    Task<string> GetTextAsync();                      // 현재 편집 내용(md 또는 plain)
    Task<string> SerializeForSaveAsync(string originalLf);   // md: round-trip 적용 결과, plain: 그대로
    Task ExecuteAsync(EditorCommand cmd);             // 서식·삽입·찾기 등
    Task SetReadOnlyAsync(bool ro);
    Task SetThemeAsync(EditorTheme t);
    Task FocusAsync();
    event EventHandler<ContentChangedEventArgs> ContentChanged;      // dirty, counts
    event EventHandler<SelectionContextEventArgs> SelectionChanged;  // 툴바 토글 상태
    event EventHandler<OutlineEventArgs> OutlineChanged;
    event EventHandler<HostRequestEventArgs> HostRequested;          // 이미지 저장, 링크 열기, 단축키
}
```
구현체: `WebEditorSurface`(WebView2+Milkdown), `PlainTextSurface`(TextBox). 툴바·상태바·저장 로직은 인터페이스만 본다.
구현 메모(T-15): 실제 시그니처는 `Editing/IEditorSurface.cs` — `ExecuteAsync(string method, object? p)`로 브릿지 메서드명을 그대로 쓰고, `MarkSavedAsync()`가 저장 후 dirty 기준선을 재설정한다. 표면은 `DocumentTab` 뷰가 생성해 `DocumentViewModel.AttachSurfaceAsync`로 붙인다(WebView2는 탭 전환 시 재부모화하지 않도록 `ContentHost`에 상주하고 Visibility만 바꾼다; `TabView`는 헤더 전용).

#### FormatToolbar (Controls)
- `CommandBar` 기반, 두 층(기본 12개 + 오버플로). 각 버튼은 `ToolbarViewModel`의 `EditorCommand`에 바인딩, 토글 상태는 `SelectionContext`(bold/italic/strike/code/headingLevel/listType/blockquote/inTable/inCodeBlock/codeLang)로 갱신.
- 컨텍스트 바(`TableContextBar`, `CodeBlockContextBar`)는 WebView2 **위에 겹치는 XAML 팝업이 아니라 JS 측 DOM**으로 그린다 (WebView2는 airspace 문제로 XAML 오버레이가 어렵다). 툴바 본체만 XAML.

#### 단축키 라우팅
- WebView2에 포커스가 있으면 키 입력은 브라우저가 소비한다. 따라서 **편집 단축키(Ctrl+B 등)는 JS keymap**이 처리하고, **앱 단축키(Ctrl+S/N/O/W/Tab/,)는 JS keymap이 가로채 `host.shortcut` 이벤트로 올린다**. XAML `KeyboardAccelerator`는 WebView2 밖(탭 헤더, 패널)에 포커스가 있을 때만 동작하므로 둘 다 등록한다.
- PlainTextHost는 XAML 가속기만 사용.

#### PdfExportService (App 계층)
1. 활성 탭의 WebView2에 `export.preparePrint({theme, pageSize})` 호출 → JS가 `print.css` 적용·UI 숨김·readonly.
2. `CoreWebView2.PrintToPdfAsync(path, CoreWebView2PrintSettings{ PageWidth/Height, Margins, ShouldPrintBackgrounds=true })`.
3. `export.restore()`.
- 인쇄(`Ctrl+P`)는 `CoreWebView2.ShowPrintUI()` 또는 동일 파이프라인으로 PDF 생성 후 시스템 인쇄.

#### JumpListService
`Windows.UI.StartScreen.JumpList.LoadCurrentAsync()` → `Recent` 카테고리에 MRU 상위 10개 (`JumpListItem.CreateWithArguments(path, name)`). MSIX 패키지 앱에서만 동작하므로 미패키지 디버그 실행 시 no-op.

### 3.3 MarkPad.Editor.Web

#### 부트스트랩 (main.ts)
```ts
const crepe = new Crepe({
  root: '#editor',
  defaultValue: '',
  features: {
    [Crepe.Feature.Toolbar]: false,     // 선택 툴바는 네이티브 툴바로 대체
    [Crepe.Feature.TopBar]: false,
    [Crepe.Feature.BlockEdit]: false,   // 드래그 핸들·슬래시 메뉴는 P2에서 재검토
    [Crepe.Feature.AI]: false,
    [Crepe.Feature.Placeholder]: true,
    [Crepe.Feature.Latex]: settings.extMath,
    // CodeMirror, ListItem, LinkTooltip, Cursor, ImageBlock, Table: true (기본)
  },
  featureConfigs: { /* 이미지 업로드 핸들러 → bridge, 코드블록 언어 목록, 링크 툴팁 문구 */ },
})
crepe.editor
  .use(frontmatterPlugin)     // §3.3 front matter
  .use(imageResolverPlugin)
  .use(mermaidPlugin)
  .use(findPlugin)
  .use(outlinePlugin)
  .use(hostKeymapPlugin)      // 앱 단축키 가로채기
  .use(highlightPlugin(settings.extHighlight))   // ==mark== (OFF면 등록 안 함)
crepe.on(l => l.markdownUpdated((ctx, md, prev) => bridge.emit('changed', {...})))
await crepe.create()
bridge.emit('ready', { version })
```
- Crepe `Toolbar`/`TopBar`를 끄는 이유: 서식 UI는 PRD 4.1대로 네이티브 XAML 툴바가 담당한다. 이중 UI를 피한다.
- 툴바 명령은 `commands.ts`에서 `editor.action(callCommand(xxxCommand.key, payload))`로 실행한다. 사용하는 커맨드 키(`@milkdown/kit/preset/commonmark`, `/preset/gfm`): `toggleStrongCommand`, `toggleEmphasisCommand`, `toggleInlineCodeCommand`, `toggleStrikethroughCommand`, `wrapInHeadingCommand(level)`, `turnIntoTextCommand`, `wrapInBulletListCommand`, `wrapInOrderedListCommand`, `wrapInBlockquoteCommand`, `createCodeBlockCommand`, `insertHrCommand`, `insertTableCommand`, `toggleLinkCommand`, `insertImageCommand`, `sinkListItemCommand`, `liftListItemCommand`. 체크리스트는 `listItem` 노드의 `checked` 속성 토글로 구현(M0에서 실제 export 이름 확인 — 부록 A 참고).

#### 브릿지 (bridge.ts) — 프로토콜은 §4

#### 이미지 처리
- 붙여넣기/드롭: Crepe `ImageBlock` feature의 `onUpload(file)` 훅 → `bridge.call('asset.save', {bytesBase64, mime})` → 호스트가 `assets/...` 상대 경로 반환 → 노드 `src`에 저장.
- 렌더: `imageResolverPlugin`이 `<img src>`를 표시용 URL로 변환한다. 모델(`src` 속성)은 원본 문자열을 유지하고, DOM에만 변환값을 쓴다.

| src 형태 | 표시 URL |
|---|---|
| 상대 경로 `assets/a.png` | `https://doc.markpad/assets/a.png` (호스트가 문서 폴더를 `doc.markpad`에 매핑, 탭마다 재매핑) |
| 절대 로컬 `C:\x\a.png`, `file:///C:/x/a.png` | `https://drive-c.markpad/x/a.png` (드라이브 문자별 지연 매핑) |
| `http(s)://` | `settings.images.allowRemote`가 true면 그대로, false면 placeholder + 툴팁 |
| `data:` | 그대로 |

#### Front matter 플러그인
- 파서: `remark-frontmatter(['yaml'])` → mdast `yaml` 노드 → ProseMirror `frontmatter` 노드(attrs: `{ raw: string }`), 문서 첫 블록에만 허용(스키마 `content: "frontmatter? block+"`).
- 직렬화: `---\n{raw}\n---\n`.
- NodeView (plain DOM, 프레임워크 없음):
  - 접힘 헤더: 상위 3개 키 `key: value` 요약, 클릭 시 펼침.
  - 펼침: `yaml.parseDocument(raw)`의 노드 트리를 재귀 렌더. `YAMLMap`→하위 행, `YAMLSeq`→칩 목록(스칼라 배열) 또는 하위 행(객체 배열), `Scalar`→타입별 컨트롤(문자열 input, 숫자 input, 불리언 토글, ISO 날짜 date input).
  - 편집은 `Document` 객체를 직접 수정(`doc.setIn(path, value)`, `deleteIn`, 키 rename은 Pair 교체) 후 `doc.toString()`으로 `raw` 갱신 → 주석·순서·들여쓰기 보존.
  - 앵커/별칭/태그/멀티 문서/파싱 오류: 트리 대신 `<textarea>` raw 편집 + 경고 배지.
- `settings.markdown.frontMatter=false`면 플러그인을 등록하지 않아 `---` 블록은 수평선+문단으로 보인다.

#### 찾기/바꾸기 플러그인
- `find.set({query, caseSensitive, wholeWord})` → 문서 텍스트 스캔 → `Decoration.inline` 하이라이트, 현재 매치 강조, `{count, index}` 이벤트.
- `find.next/prev/replace/replaceAll` — 코드블록·front matter raw 포함, 이미지 alt 제외.

#### 개요(Outline) 플러그인
- 트랜잭션마다 `heading` 노드를 수집 → `[{level, text, pos}]`를 200ms 디바운스로 `outline` 이벤트. `outline.goto(pos)`로 스크롤+커서 이동.

#### Mermaid 플러그인
- `code_block` NodeView를 lang=mermaid일 때 확장: 편집 영역 아래 미리보기 div. `mermaid.render` 결과 SVG 삽입, 오류 시 오류 문자열. 300ms 디바운스.

#### 내보내기 (export/)
- `export.renderHtml({inlineImages, theme})`: 현재 마크다운을 `unified().use(remarkParse).use(remarkGfm).use(remarkMath).use(remarkRehype).use(rehypeKatex).use(rehypeShiki).use(rehypeStringify)`로 정적 HTML 변환, mermaid 블록은 SVG로 치환, 이미지는 옵션에 따라 호스트에 base64 요청. 결과 `{html, css}`를 호스트가 단일 파일로 조립.
- `export.preparePrint/restore`: PDF 파이프라인용.

#### 테마
- 호스트가 `theme.set({mode, fontFamily, fontSize, lineHeight, maxWidth, zoom, accent})` 호출 → CSS 변수 갱신. 다크 모드는 `data-theme="dark"` + Crepe 테마 CSS(frame 테마 기반 커스텀).

---

## 4. 브릿지 프로토콜 (C# ↔ JS)

### 4.1 전송
- host→web: `CoreWebView2.PostWebMessageAsJson(json)`; web→host: `window.chrome.webview.postMessage(obj)`.
- 번들은 `https://app.markpad/index.html`에서 로드 (`SetVirtualHostNameToFolderMapping("app.markpad", Assets/editor, DenyCors)`). 문서 폴더는 `doc.markpad`(Allow).
- 보안: `NavigationStarting`에서 `app.markpad` 외 내비게이션 취소, `NewWindowRequested` 취소 후 `Launcher.LaunchUriAsync`, `Settings.AreDevToolsEnabled = Debug만`, `IsWebMessageEnabled = true`, `AreDefaultContextMenusEnabled = false`(JS 컨텍스트 메뉴 사용).

### 4.2 메시지 형식 (JSON-RPC 2.0 축약)
```jsonc
// 요청 (양방향)
{ "t": "req", "id": 17, "m": "format.toggle", "p": { "mark": "bold" } }
// 응답
{ "t": "res", "id": 17, "ok": true, "r": { /* 결과 */ } }
{ "t": "res", "id": 17, "ok": false, "e": { "code": "NOT_READY", "msg": "..." } }
// 이벤트 (단방향)
{ "t": "evt", "m": "changed", "p": { "dirty": true, "words": 1234, "chars": 6789 } }
```
- `id`는 방향별 독립 증가. 요청 타임아웃 10초(로드는 60초). 미응답은 `BridgeException`.
- `ready` 이벤트 이전 요청은 호스트에서 큐잉.

### 4.3 host → web 메서드
| 메서드 | 파라미터 | 결과 | PRD |
|---|---|---|---|
| `doc.load` | `{text, path?, readonly, settings}` | `{ok}` | F-FILE-01 |
| `doc.getMarkdown` | — | `{text}` | |
| `doc.serializeForSave` | `{original}` | `{text, changedBlocks}` | G4, §5 |
| `doc.setReadonly` | `{value}` | | F-VIEW-09 |
| `doc.markSaved` | — | | 저장 성공 후 dirty 기준선 재설정(커서·히스토리 유지) |
| `doc.isDirty` | — | `{dirty}` | |
| `doc.rewriteAssetPaths` | `{map: {old:new}}` | `{count}` | F-IMG-04 |
| `format.toggle` | `{mark: bold\|italic\|strike\|code\|highlight}` | | 부록 A |
| `format.heading` | `{level: 0..6}` | | |
| `format.list` | `{type: bullet\|ordered\|task}` | | |
| `format.indent` / `format.outdent` | — | | |
| `format.blockquote` / `format.clear` | — | | |
| `insert.codeBlock` | `{lang?}` | | |
| `insert.hr` / `insert.table` | `{rows, cols}` | | |
| `insert.link` | `{text?, href}` | | |
| `insert.image` | `{src, alt?}` | | |
| `insert.math` | `{display: bool}` | | |
| `insert.footnote` / `insert.datetime` | `{text}` | | |
| `table.*` | `addRowAbove/Below, addColLeft/Right, delRow, delCol, align{dir}, delete, toggleHeader` | | F-EDIT-07 |
| `find.set/next/prev/replace/replaceAll/clear` | `{query, caseSensitive, wholeWord, replacement?}` | `{count, index}` | F-EDIT-11 |
| `outline.goto` | `{pos}` | | F-VIEW-05 |
| `view.setTheme` | `{mode, font…, zoom}` | | F-VIEW-08 |
| `view.focus` / `view.scrollToTop` | | | |
| `block.showSource` | `{pos}` | | F-EDIT-12 |
| `export.renderHtml` | `{inlineImages, theme}` | `{html, css}` | F-EXP-01 |
| `export.preparePrint` / `export.restore` | `{pageSize}` | | F-EXP-02 |
| `history.undo` / `history.redo` | | | F-EDIT-06 |
| `edit.cut/copy/paste/selectAll` | | | |

### 4.4 web → host
| 종류 | 메서드 | 파라미터 |
|---|---|---|
| evt | `ready` | `{version}` |
| evt | `changed` | `{dirty, words, chars, line}` (150ms 디바운스) |
| evt | `selection` | `{bold, italic, strike, code, highlight, heading, list, blockquote, inTable, inCode, codeLang, link}` |
| evt | `outline` | `[{level, text, pos}]` |
| evt | `find.result` | `{count, index}` |
| evt | `shortcut` | `{key: "ctrl+s" …}` — 앱 단축키 위임 |
| evt | `log` | `{level, msg}` |
| evt | `files.dropped` | `{files: [{name, text}]}` — 편집기 위에 md/txt를 드롭한 경우. WebView2는 파일 경로를 노출하지 않으므로 호스트가 **제목 없는 복사본**으로 연다. 탭 줄·툴바·상태바 드롭은 XAML `Drop`으로 원본 경로를 연다 |
| req | `asset.save` | `{bytesBase64, mime, suggestedName?}` → `{relPath}` |
| req | `asset.pick` | — → `{relPath, alt}` (파일 대화상자) |
| req | `asset.readBase64` | `{src}` → `{dataUrl}` (HTML 내보내기 임베드용) |
| req | `link.open` | `{href}` → `{ok}` (외부 브라우저 또는 md 새 탭) |
| req | `image.resolve` | `{src}` → `{url}` (드라이브 매핑이 필요한 절대 경로) |

C# DTO는 `Bridge/BridgeMessages.cs`에 `record`로 정의하고 `System.Text.Json` source generator를 쓴다. JS 타입은 `bridge-types.ts`에 동일 이름으로 정의하며, 양쪽 스키마 드리프트를 막기 위해 `npm run gen:types`로 C# → TS 타입을 생성한다(M1, 선택).

---

## 5. Round-trip 원본 보존 알고리즘 (PRD §5.3)

목표: 사용자가 손대지 않은 블록은 저장 시 원본 바이트 그대로.

```
입력: original (LF 정규화 원본), current = crepe.getMarkdown()
1. A = parse(original), B = parse(current)      // unified + remark-parse + gfm + frontmatter + math
   → 최상위 블록 노드 배열 (position.start.offset / end.offset 포함)
2. 각 노드에 대해 key = canonical(node)          // position 제거, 공백 정규화, 텍스트·구조만 남긴 mdast → JSON 문자열
3. LCS(A.keys, B.keys)로 정렬(alignment) 계산
4. 출력 조립:
   for 각 정렬 항목:
     - 일치 (A[i] ≡ B[j]) → original.slice(A[i].start, A[i].end)   // 원본 그대로
     - B에만 있음(신규/변경) → current.slice(B[j].start, B[j].end)
     - A에만 있음(삭제) → 생략
   블록 사이 구분: 일치 블록 뒤에는 원본의 후행 공백(다음 블록 시작 전까지)을 그대로, 그 외는 "\n\n"
5. front matter: A[0]이 yaml이고 B[0]도 yaml이면 raw 비교 — 동일하면 원본, 다르면 B
6. 파일 끝: settings.save.ensureTrailingNewline 또는 원본 EndsWithNewline 규칙 적용 (호스트)
반환: {text, changedBlocks: B에서 대체된 인덱스 목록}
```
- `canonical()`은 ProseMirror 문서가 표현할 수 있는 것만 비교한다 (M0 코퍼스 103/103 diff=0, ADR-011 §3):
  - 인라인 콘텐츠는 **마크 런**(텍스트 + 정렬된 마크 집합 `emphasis|strong|delete|link:url:title`)으로 평탄화 → `[_a_](u)` ≡ `_[a](u)_`, `[**b**](u)` ≡ `**[b](u)**`. 마크 경계의 공백은 마크 밖으로 옮긴 뒤 비교(`[*a* b](u)` ≡ `*[a](u)* [b](u)`).
  - `linkReference`/`imageReference`는 문서의 `definition`으로 해석해 `link`/`image`와 같게 취급. `definition` 블록은 편집기가 생성할 수 없으므로 정렬에서 A에만 있어도 **항상 원본에서 유지**한다.
  - 포함: `table.align`(행별 후행 빈 셀 제거 후 최대 폭으로 정규화), `code.lang/meta/value`, 링크 `url/title`, 이미지 `url/alt/title`, `heading.depth`, `listItem.checked`, `html.value`(공백·자기닫힘 정규화: `<br >` ≡ `<br />`).
  - 무시: 강조·목록 기호, 이스케이프, 표 정렬 공백(AST에 없음), `list/listItem.spread`, 텍스트·인라인 코드 내부 공백 차이, `<br />`만 있는 표 셀.
- 엔진 보정(`crepe-factory.ts`): `remarkPreserveEmptyLinePlugin` 제거(빈 문단/셀 `<br />` 방지), `image-alt`(alt 보존), `code-meta`(info string 보존).
- 리스트 하나가 최상위 블록이므로, 항목 하나만 고쳐도 리스트 전체가 재직렬화된다. 이를 완화하기 위해 `list`와 `blockquote`는 **한 단계 더 내려가서 자식 단위로 재귀 정렬**한다(2단계 한정).
- 호스트 측 후처리: EOL 복원, BOM, 끝 개행.
- 검증: `corpus/roundtrip/*.md` 각각을 load→serializeForSave(무편집) 했을 때 `diff == 0`이어야 한다(vitest). 편집 케이스는 "H1 하나 수정 시 변경 블록 수 == 1" 류의 스냅샷 테스트.

---

## 6. 주요 데이터 흐름

### 6.1 파일 열기
```
탐색기 더블클릭 → AppInstance 리다이렉트 → ActivationService → ShellVM.OpenFilesAsync
 → DocumentIO.OpenAsync (인코딩·EOL 감지) → DocumentVM 생성 → 탭 추가
 → Kind==Markdown ? WebEditorSurface : PlainTextSurface
 → WebEditorSurface: WebView2 초기화(공유 환경) → doc.markpad 매핑 → navigate app.markpad
 → 'ready' 수신 → doc.load(textLf) → 'outline'/'changed' 수신 → 상태바 갱신 → MRU 추가
```
### 6.2 저장
```
Ctrl+S (JS keymap → 'shortcut' evt) → ShellVM.SaveAsync
 → path 없으면 FileSavePicker (WASDK 2.x: 빈 파일 자동 생성 안 됨 → 경로만 받음)
 → pending 이미지 있으면 AssetService.RelocatePendingAsync → doc.rewriteAssetPaths
 → doc.serializeForSave(originalLf) → DocumentIO.SaveAsync (EOL/BOM/개행) → AtomicWriter
 → Document.OriginalText 갱신, dirty=false, watcher 기준 시각 갱신, 복구 스냅샷 삭제
```
### 6.3 이미지 붙여넣기
```
Ctrl+V (JS) → ImageBlock onUpload(file) → req asset.save(base64)
 → AssetService.SaveImageAsync → assets/ 저장 → {relPath}
 → JS: 노드 src=relPath, DOM src=https://doc.markpad/relPath
```
### 6.4 외부 변경
```
FileSystemWatcher → 디바운스 → DocumentVM.OnExternalChange
 → dirty==false: 조용히 다시 로드 (커서 위치 복원 시도)
 → dirty==true: InfoBar "파일이 외부에서 변경됨 [다시 읽기] [무시]"
```

---

## 7. 횡단 관심사

### 7.1 오류 처리
- 브릿지 예외는 `BridgeException(code)`로 래핑, UI에는 `InfoBar`로 표시. `NOT_READY`·`TIMEOUT`은 자동 1회 재시도.
- WebView2 `ProcessFailed` → 탭 표면 재생성 → 마지막 스냅샷 로드 → InfoBar 안내.
- 전역 `UnhandledException` 로그 + 모든 dirty 탭 스냅샷 강제 저장.

### 7.2 복구 스냅샷
- `RecoveryStore`: dirty 탭마다 5초 디바운스로 `%LOCALAPPDATA%\MarkPad\recovery\{docId}.md` + `{docId}.json`(원본 경로, 시각). 정상 종료·저장 시 삭제. 시작 시 잔존 파일이 있으면 복구 대화상자.

### 7.3 성능
- 에디터 번들은 앱 시작 시가 아니라 **첫 md 탭 생성 시** 로드. WebView2 환경은 시작 직후 백그라운드로 워밍업.
- `changed` 이벤트는 마크다운 문자열을 싣지 않는다(직렬화는 저장 시에만). 단어 수는 ProseMirror 텍스트 기준.
- 1MB 초과 문서: 로드 전 InfoBar 경고, Mermaid·수식 미리보기 지연 렌더.
- 번들 크기 목표 ≤ 3MB gzip 이전 기준 (Mermaid는 동적 import).

### 7.4 보안
- WebView2에서 원격 내비게이션 차단, 스크립트가 있는 인라인 HTML은 DOMPurify로 렌더(편집 불가 raw 블록).
- 원격 이미지 로드는 설정 의존, 기본 허용(PRD).
- 로그에 문서 내용 미기록.

### 7.5 접근성·현지화
- 툴바 버튼 `AutomationProperties.Name`, 툴팁, 키보드 탐색. 문자열은 `Resources.resw`(ko-KR 기본, en-US 보조).

---

## 8. 빌드·개발 환경

### 8.1 요구 도구
.NET 10 SDK (10.0.400 이상), Node.js 22 LTS, Windows App SDK 2.4 런타임. Visual Studio는 **선택**이다: XAML 컴파일러·Windows SDK 빌드 도구가 NuGet(`Microsoft.WindowsAppSDK`, `Microsoft.Windows.SDK.BuildTools`)으로 오므로 `dotnet build`만으로 빌드·실행된다. 개발 빌드는 언패키지드 + `WindowsAppSDKSelfContained=true`(`-p:Packaged=true`로 MSIX). 실행·스크린샷: `build/run-dev.ps1 [-Configuration Release] [-File x.md] [-Screenshot out.png]`. C# `LangVersion`은 `preview`(CommunityToolkit.Mvvm 8.4 partial property 요구).

### 8.2 빌드 파이프라인
```
MarkPad.App.csproj
  <Target Name="BuildEditorWeb" BeforeTargets="PrepareForBuild"
          Condition="'$(SkipEditorWeb)' != 'true'">
    <Exec Command="npm ci" WorkingDirectory="../MarkPad.Editor.Web" Condition="!Exists('../MarkPad.Editor.Web/node_modules')"/>
    <Exec Command="npm run build" WorkingDirectory="../MarkPad.Editor.Web"/>
  </Target>
  <ItemGroup><Content Include="Assets/editor/**" CopyToOutputDirectory="PreserveNewest"/></ItemGroup>
```
- Vite `build.outDir = ../MarkPad.App/Assets/editor`, `base = './'`, 단일 청크(Mermaid만 동적).
- 개발 시 `npm run dev`로 Vite 서버를 띄우고 `MARKPAD_EDITOR_URL=http://localhost:5173`이면 앱이 virtual host 대신 해당 URL을 로드(HMR).

### 8.3 테스트 전략
| 계층 | 도구 | 대상 |
|---|---|---|
| Core 단위 | xUnit | 인코딩/EOL 감지, AtomicWriter, AssetNaming, Settings 직렬화, MRU |
| Web 단위 | vitest (jsdom) | roundtrip(코퍼스 150개 diff=0), front matter 편집→YAML 보존, 커맨드 매핑 |
| 통합 | xUnit + WebView2 headless 불가 → App 프로젝트 내 `BridgeSmokeTests` (실제 창) | load→save round-trip, 이미지 붙여넣기 경로 |
| UI 스모크 | FlaUI (M2) | 열기·편집·저장·탭·내보내기 |
| 수동 | 체크리스트 (IMPLEMENTATION-PLAN 부록) | 한글 IME, 성능 수치 |

### 8.4 CI (GitHub Actions, windows-latest)
`dotnet restore` → `npm ci && npm test` → `dotnet build -c Release` → `dotnet test` → MSIX 생성(자체 서명) → 아티팩트 업로드. main 브랜치 태그 시 릴리스.

---

## 9. 아키텍처 결정 기록 (ADR 요약)

| ID | 결정 | 대안 | 근거 |
|---|---|---|---|
| ADR-01 | WinUI 3 셸 + WebView2/Milkdown 편집기 (하이브리드) | 순수 XAML 에디터, Electron/Tauri | PRD §5.1. 셸은 네이티브, 편집 품질은 웹 생태계 |
| ADR-02 | Crepe 사용, 내장 Toolbar/TopBar/BlockEdit 비활성 | Milkdown core만 조립 | 표·코드·이미지·리스트 UX가 이미 완성돼 M0 기간 단축. 서식 UI는 네이티브로 통일 |
| ADR-03 | Round-trip을 AST 동등성 기반 블록 치환으로 구현(변경 추적 없음) | ProseMirror 트랜잭션 기반 dirty 블록 추적 | 상태 없이 저장 시점에만 계산 → 단순·검증 용이. 리스트/인용은 2단계 재귀로 보완 |
| ADR-04 | YAML 편집에 `yaml` 패키지 Document API | js-yaml (dump/load) | 주석·키 순서·인용 스타일 보존은 Document 모델만 가능 |
| ADR-05 | 이미지 표시를 virtual host 매핑 + NodeView src 변환으로 처리 | `<base href>`, file:// 허용 | 모델의 상대 경로를 유지하면서 보안 경계(app.markpad) 유지 |
| ADR-06 | 편집 단축키는 JS, 앱 단축키는 JS→host 위임 | XAML 가속기 단독 | WebView2 포커스 시 XAML 가속기 미동작 |
| ADR-07 | 컨텍스트 툴바(표·코드)는 JS DOM | XAML 팝업 오버레이 | WebView2 airspace 제약 |
| ADR-08 | .txt는 네이티브 TextBox | 동일 WebView2에 plain 모드 | PRD F-FILE-11, 메모장 수준 가벼움 |
| ADR-09 | .NET 10 LTS + Windows App SDK 2.4 | .NET 8 + WASDK 1.7 | 2028년까지 LTS, 2.x의 TitleBar·Picker 개선 활용 |
| ADR-10 | 단일 인스턴스 + 활성화 리다이렉트 | 다중 인스턴스 | 탐색기에서 여러 파일을 열 때 탭으로 모이는 것이 PRD 탭 모델에 부합 |

---

## 부록 A. M0에서 확인할 라이브러리 사실 (가정 목록) — 2026-09-04 검증 완료
검증 상세와 근거는 `docs/ADR/ADR-011-m0-findings.md`.
1. **수정됨** — 체크리스트 전용 커맨드 없음. `list_item.checked`(`boolean|null`) 속성을 `tr.setNodeMarkup`으로 토글 (`commands.ts setList`).
2. **확인됨** — `onUpload`/`inlineOnUpload`/`blockOnUpload(file) => Promise<string>` + `proxyDomURL(url)`. 단, image-block은 md `alt`에 종횡비를 쓰므로 `plugins/image-alt.ts`로 alt를 보존한다.
3. **확인됨** — `CodeBlockConfig.languages`, `renderPreview(language, content, apply)` 훅으로 mermaid 미리보기 부착 가능(feature 교체 불필요). 코드펜스 `meta`는 `plugins/code-meta.ts`로 보존.
4. **확인됨(직렬화함)** — `updated`만 구독, 직렬화는 저장 시에만. `changed` 150ms / `selection` 50ms 디바운스.
5. **확인됨** — `WebView2.EnsureCoreWebView2Async(CoreWebView2Environment)` 오버로드 존재, `CreateWithOptionsAsync(null, userDataFolder, options)`.
6. **수동 검증 대기** — `src/MarkPad.Editor.Web/test/ime-notes.md` 체크리스트.
7. **확인됨** — `Microsoft.Windows.Storage.Pickers.FileSavePicker(WindowId).PickSaveFileAsync()` → `PickFileResult.Path`(파일 미생성).
8. **한계(신규)** — `list_item`은 제목을 첫 자식으로 허용하지 않아 `- ### 제목` 구조가 분해됨. M2 스키마 확장 검토.
