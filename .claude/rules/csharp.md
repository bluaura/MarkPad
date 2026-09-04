---
paths:
  - "src/MarkPad.App/**/*.cs"
  - "src/MarkPad.Core/**/*.cs"
  - "tests/**/*.cs"
---
# C# 규칙 (MarkPad.App / MarkPad.Core)

- `MarkPad.Core`는 `Microsoft.UI.*`, `Windows.UI.*`, `WinRT`를 참조하지 않는다. 파일 대화상자·WebView2·점프리스트는 App 계층.
- 파일 저장은 항상 `AtomicWriter`를 거친다. `File.WriteAllText` 직접 호출 금지.
- 텍스트는 Core 내부에서 LF 정규화 상태로 다루고, EOL/BOM 복원은 `DocumentIO.SaveAsync`에서만 한다.
- WebView2 호출은 `EditorBridge`를 통해서만. `CoreWebView2.ExecuteScriptAsync` 직접 호출 금지(디버그 제외).
- 비동기 메서드는 `Async` 접미사, `ConfigureAwait(false)`는 Core에서만.
- 새 서비스는 `App.Services`(DI)에 등록하고 생성자 주입. 정적 싱글턴 금지.
- 예외는 `BridgeException`/`DocumentIOException` 등 도메인 예외로 래핑해 ViewModel까지 올리고, UI는 `InfoBar`로 표시.
- 문자열 리소스는 `Resources.resw`(ko-KR 기본). 하드코딩 UI 문자열 금지(로그 제외).
- xUnit 테스트: `Arrange/Act/Assert` 주석, 픽스처는 `corpus/fixtures/` 사용, 테스트 파일은 임시 폴더에 복사 후 사용(원본 불변).
