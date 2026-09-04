# T-10 한글 IME 검증 노트

| 항목 | 내용 |
|---|---|
| 상태 | **수동 검증 대기** — 자동화 불가(IME 조합은 SendKeys/UI 자동화로 재현되지 않음) |
| 대상 빌드 | `build/run-dev.ps1` (Debug, 언패키지드) |
| 검증자 / 일자 | (기입) |

## 절차

1. `dotnet build src/MarkPad.App -c Debug` 후 `build/run-dev.ps1 -KeepRunning`으로 실행한다.
2. 한국어 IME(Microsoft 입력기) 두벌식으로 전환하고 아래 표의 각 항목을 수행한다.
3. 실패 항목은 재현 md 파일을 `corpus/fixtures/ime-*.md`로 남기고 회피책을 기록한다.
4. DevTools(F12, Debug 빌드)에서 `changed` 이벤트 수를 확인하려면 콘솔에서
   `window.__markpadHost` 대신 호스트 로그(`%LOCALAPPDATA%\MarkPad\logs`)의 `changed` 디버그 항목을 본다.

## 체크리스트 (IMPLEMENTATION-PLAN §6.1)

| # | 항목 | 기대 | 결과 |
|---|---|---|---|
| 1 | 문단에서 "안녕하세요" 조합 입력 | 조합 중 글자가 깨지지 않고 완성됨 | |
| 2 | 조합 중 백스페이스 | 자모 단위로 삭제, 앞 글자 손상 없음 | |
| 3 | 표 셀 안에서 한글 입력 | 셀 밖으로 커서 이탈 없음 | |
| 4 | 코드블록(CodeMirror) 안에서 한글 입력 | 정상 조합 | |
| 5 | 링크 텍스트 안에서 한글 입력 | 링크 마크 유지 | |
| 6 | 한자 변환(한자 키) | 후보 창 표시·선택 반영 | |
| 7 | 영문↔한글 전환 직후 타이핑 | 첫 글자 누락 없음 | |
| 8 | 세벌식 최종 | 1~2 항목 반복 | |
| 9 | `changed` 이벤트가 조합 중 문자마다 발생하지 않음 | 150ms 디바운스로 흡수(로그 확인) | |
| 10 | 조합 중 Ctrl+B 등 서식 단축키 | 조합 확정 후 적용, 깨짐 없음 | |

## 설계상 참고

- 앱 단축키 가로채기(`host-keymap.ts`)는 `KeyboardEvent.isComposing`이 true이면 개입하지 않는다.
- `changed`/`selection` 이벤트는 각각 150ms/50ms 디바운스된다 (`editor.ts`).
- ProseMirror는 `compositionstart/end`를 자체 처리한다. 문제가 있으면 IMPLEMENTATION-PLAN §7 R2의
  `handleDOMEvents` 억제 방식을 적용한다.
