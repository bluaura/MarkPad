---
paths:
  - "src/MarkPad.Editor.Web/**"
---
# 웹 에디터 규칙 (MarkPad.Editor.Web)

- Milkdown API는 `@milkdown/kit/*` 경로에서만 import. 개별 `@milkdown/preset-*` 패키지 직접 설치 금지(버전 충돌).
- Crepe의 `Toolbar`/`TopBar`/`BlockEdit`/`AI` feature는 끈 상태를 유지한다(ADR-02). 서식 UI는 네이티브 툴바.
- ProseMirror 상태 변경은 커맨드(`callCommand`) 또는 트랜잭션으로만. DOM 직접 조작으로 콘텐츠 변경 금지.
- NodeView는 플레인 DOM으로 작성(프레임워크 없음). Crepe 내부의 Vue 컴포넌트를 재사용하려 하지 말 것.
- `changed` 이벤트에 마크다운 문자열을 싣지 않는다. 직렬화는 `doc.getMarkdown`/`doc.serializeForSave` 요청 시에만.
- `roundtrip.ts` 수정 시 `npm test -- roundtrip` 코퍼스 전체 통과 필수. `canonical()` 규칙 변경은 ADR로 기록.
- 이미지 `src` 모델 값은 원본 문자열 유지, 표시 URL 변환은 `plugins/image-resolver`에서만.
- 앱 단축키(Ctrl+S/N/O/W/Tab 등)는 `hostKeymapPlugin`에서 `shortcut` 이벤트로 위임. `preventDefault` 후 브라우저 기본 동작 차단.
- 외부 리소스 fetch 금지(오프라인 번들). Mermaid는 동적 import.
- 새 브릿지 메서드: `src/handlers/<domain>.ts`에 등록 + `bridge-types.ts` 타입 + C# DTO + ARCHITECTURE §4.
