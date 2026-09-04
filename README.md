# MarkPad

Windows용 WYSIWYG Markdown 뷰어·에디터. WinUI 3 셸 + WebView2/Milkdown 편집기 (문서: `docs/`).

## 요구 도구

- .NET 10 SDK (10.0.400+), Node.js 22 LTS, Windows App SDK 2.4 런타임 (Windows 11에 보통 설치됨)
- Visual Studio는 선택. `dotnet` CLI만으로 빌드·실행 가능.

## 빌드·실행

```powershell
# 편집기 웹 번들 + 앱 (BuildEditorWeb 타깃이 npm run build를 먼저 실행)
dotnet build src/MarkPad.App/MarkPad.App.csproj -c Debug

# 실행 (선택: 파일 열기 / 스크린샷)
build/run-dev.ps1 -KeepRunning
build/run-dev.ps1 -File corpus/fixtures/utf8-lf.md -Screenshot out.png

# 웹 번들 재빌드를 건너뛰고 앱만
dotnet build src/MarkPad.App/MarkPad.App.csproj -p:SkipEditorWeb=true
```

편집기만 브라우저에서 개발: `cd src/MarkPad.Editor.Web && npm run dev` 후 앱을 `MARKPAD_EDITOR_URL=http://localhost:5173` 환경 변수로 실행하면 Vite HMR을 사용한다.

## 테스트

```powershell
dotnet test                                   # MarkPad.Core (인코딩·EOL·원자적 저장)
cd src/MarkPad.Editor.Web; npm test           # roundtrip 단위 + 코퍼스 103개 (Crepe/jsdom)
RT_FILE=name.md npx vitest run test/debug-roundtrip.spec.ts   # 특정 코퍼스 파일 불일치 진단
```

코퍼스 결과는 `docs/perf/roundtrip-report.md`에 기록된다.

## 구조

`docs/ARCHITECTURE.md` §2.3 참조. 요약: `src/MarkPad.App`(WinUI 3), `src/MarkPad.Core`(문서 IO 등 순수 .NET), `src/MarkPad.Editor.Web`(TypeScript, Vite → `MarkPad.App/Assets/editor`), `tests/`, `corpus/`, `docs/`.
