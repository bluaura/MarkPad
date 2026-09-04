---
name: verify
description: 전체 검증(scripts/verify.ps1)을 실행하고 실패를 원인별로 분류해 고친다. "검증해줘", "테스트 돌려줘", "빌드 확인" 요청에 사용.
argument-hint: [quick]
---
1. `$ARGUMENTS`가 `quick`이면 `pwsh scripts/verify.ps1 -Quick`, 아니면 `pwsh scripts/verify.ps1`를 실행한다.
2. 실패 단계별로 원인을 분류한다: (a) 내가 방금 바꾼 코드, (b) 기존 코드의 잠복 버그, (c) 환경(패키지 미설치, 런타임 없음), (d) 테스트 자체의 문제.
3. (a)는 즉시 고친다. (b)(d)는 고치기 전에 사용자에게 한 줄로 알린다. (c)는 해결 명령을 제시하고 멈춘다.
4. round-trip 코퍼스 실패는 **코퍼스 파일을 수정하지 않는다**. `roundtrip.ts`의 `canonical()`/정렬 로직을 의심하고, 실패 파일과 diff를 보고한다.
5. 통과하면 각 단계 소요 시간을 한 줄로 요약한다.
