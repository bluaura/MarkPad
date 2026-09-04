---
name: status
description: 저장소 상태(git log, 변경 파일, 테스트 결과)와 IMPLEMENTATION-PLAN.md를 대조해 docs/STATUS.md를 재생성한다. 하네스 첫 적용 시, 또는 STATUS.md가 실제와 어긋났을 때 "상태 갱신해줘", "/status"로 사용.
disable-model-invocation: true
---
1. `git log --oneline -50`과 커밋 메시지의 `T-xx` 태그, `src/` 트리를 보고 어떤 작업이 실제로 구현됐는지 판단한다. 커밋 메시지에 태그가 없으면 파일 존재 여부(IMPLEMENTATION-PLAN 산출물 열)로 추정하고 "추정"이라고 표기한다.
2. `pwsh scripts/verify.ps1 -Quick`를 돌려 현재 건강 상태를 확인한다.
3. `docs/STATUS.md`를 `docs/STATUS.template.md` 형식으로 다시 쓴다. 완료/진행 중/막힘/다음 작업/알게 된 것 섹션을 채운다.
4. IMPLEMENTATION-PLAN의 DoD 중 확인되지 않은 항목이 있는 "완료" 작업은 완료가 아니라 "DoD 미확인"으로 분류하고 목록을 보고한다.
5. ARCHITECTURE 부록 A 가정 중 코드에서 이미 답이 나온 항목이 있으면 "확인됨/수정됨" 후보로 보고한다 (문서는 사용자 확인 후 수정).
