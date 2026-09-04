# M0 성능 기준선 (T-11)

| 항목 | 내용 |
|---|---|
| 측정일 | 2026-09-04 |
| 빌드 | Release, 언패키지드, Windows App SDK 2.4 self-contained, 편집기 번들 v0.1.0 |
| 방법 | `build/run-dev.ps1 -Configuration Release [-File …]` 실행 후 `%LOCALAPPDATA%\MarkPad\logs` 타임스탬프와 `Get-Process` 작업 집합. 각 1회 측정(워밍업 없음), WebView2 런타임 152.0.4191.62 |
| 픽스처 | 코퍼스 README를 이어 붙인 100KB(80,599자)·1MB(1,062,310자) 문서 |

## PRD §5.4 대비

| 항목 | 목표 | 실측 | 판정 |
|---|---|---|---|
| 콜드 스타트 → 빈 문서 편집 가능 | ≤ 1.5초 | **0.77~0.86초** (프로세스 시작→`ready` 수신; 내비게이션→ready 103~122ms) | ✅ |
| 100KB md 열기 → 렌더 완료 | ≤ 1초 | **0.56초** (`doc.load` 왕복, 파싱+렌더 포함) | ✅ |
| 1MB md 열기 | ≤ 4초, UI 프리징 없음 | **2.69초** (프리징 여부는 미측정 — `doc.load`는 WebView 스레드에서 동기 파싱) | ✅ (프리징은 T-43에서 확인) |
| 타이핑 지연 | ≤ 50ms | 미측정 (DevTools Performance 수동, T-10과 함께) | ⏳ |
| 유휴 메모리 (탭 1개, WebView2 포함) | ≤ 150MB | **작업 집합 합계 540MB / 프라이빗 합계 269MB** — MarkPad.exe 158MB(프라이빗 75MB) + msedgewebview2 6개 프로세스 382MB(프라이빗 194MB) | ❌ |
| 설치 용량 | ≤ 60MB | Release 출력 폴더 153MB(self-contained 개발 빌드) → **MSIX(framework-dependent) 29.9MB** (`build/pack.ps1`, 2026-09-04) | ✅ (MSIX 기준) |

## 상세

| 케이스 | MarkPad.exe 작업 집합 | 내비게이션→ready | 프로세스 시작→ready | doc.load |
|---|---|---|---|---|
| 빈 문서 (콜드) | 155MB | 103ms | 862ms | 59ms |
| 100KB | 158MB | 113ms | 765ms | 559ms |
| 1MB | 174MB | 122ms | 809ms | 2,688ms |
| 빈 문서 (재실행) | 155MB | 114ms | 844ms | 50ms |

WebView2 프로세스 트리(빈 문서, 12초 유휴): 브라우저 117MB, 렌더러 123MB, GPU 78MB, 유틸리티 34/20/10MB (작업 집합).

## 번들

- `Assets/editor` 4.2MB (gzip 전, 179개 파일). 진입 청크 `editor.js` 1.51MB(gzip 479KB) — 목표 3MB 이내. CodeMirror 언어 모드와 Mermaid는 지연 청크.

## 미달 항목 → T-43 반영

1. **메모리**: WebView2 프로세스 6개가 기본. 후보 조치 — `AdditionalBrowserArguments`로 GPU/유틸리티 프로세스 축소(`--in-process-gpu` 등) 검토, 편집기 페이지의 KaTeX/CodeMirror 지연 로드, MarkPad.exe 자체 75MB 프라이빗 축소(불필요한 WASDK AI/ML 참조 제거 확인). PRD R3의 재협의(200MB)도 작업 집합 기준으로는 도달 어려움 → **프라이빗 바이트 기준으로 목표 재정의** 제안.
2. **설치 용량**: framework-dependent MSIX(런타임 별도) 기준으로 재측정.
3. 1MB 문서의 UI 프리징 여부·프로그레스 표시(PRD §5.4)는 T-43에서 측정.
