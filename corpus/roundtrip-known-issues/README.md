# Round-trip 알려진 예외 (코퍼스에서 분리)

IMPLEMENTATION-PLAN T-34 규정: 무편집 diff=0을 달성할 수 없는 파일은 사유를 적고 분리한다.
이 폴더의 파일은 `npm test`에서 검사하지 않는다.

| 파일 | 사유 | 영향 | 후속 |
|---|---|---|---|
| jwasham-coding-interview-university-README.md | 목록 항목의 첫 자식이 제목(`- ### Recursion`)인 구조. Milkdown `list_item` 스키마(`paragraph block*`)는 제목을 첫 자식으로 허용하지 않아 파싱 시 빈 항목 + 최상위 제목으로 분해됨. 같은 파일에서 8칸 들여쓰기 lazy continuation(`        - [Companion…`)도 문단 텍스트 ↔ 중첩 목록으로 해석이 갈림. | 해당 목록 블록 2개가 저장 시 재직렬화됨(다른 블록은 보존). | M2에서 `list_item` 스키마 확장(제목 허용) 검토 — ADR-011 항목 8 |
