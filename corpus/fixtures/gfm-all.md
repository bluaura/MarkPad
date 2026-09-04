---
title: GFM 전체 요소 픽스처
tags: [fixture, gfm]
---

# 제목 1

## 제목 2

### 제목 3

#### 제목 4

##### 제목 5

###### 제목 6

단락에는 **굵게**, *기울임*, ~~취소선~~, `인라인 코드`, [링크](https://example.com "제목"), 그리고 자동 링크 https://github.com 와 <https://example.org> 가 들어 있습니다. 각주도 있습니다[^1].

> 인용문 첫 줄
> 인용문 둘째 줄
>
> > 중첩 인용

- 글머리 항목
- 두 번째 항목
  - 중첩 항목
    - 3단계 항목
- 세 번째 항목

1. 번호 항목
2. 번호 항목
   1. 중첩 번호
3. 번호 항목

- [ ] 할 일
- [x] 완료한 일
- [ ] 중첩
  - [x] 완료된 하위 항목

| 왼쪽 | 가운데 | 오른쪽 |
|:-----|:------:|-------:|
| a | b | c |
| **굵게** | `코드` | [링크](https://example.com) |

```kotlin
fun main() {
    println("Hello, MarkPad!") // 주석
}
```

```python
def greet(name: str) -> str:
    return f"hi {name}"
```

    들여쓰기 코드 블록

---

![이미지 alt](assets/sample.png "이미지 제목")

인라인 수식 $E = mc^2$ 와 블록 수식:

$$
\int_0^1 x^2 \, dx = \frac{1}{3}
$$

```mermaid
flowchart LR
  A[열기] --> B[편집] --> C[저장]
```

<details>
<summary>인라인 HTML</summary>

숨겨진 내용

</details>

줄 끝에 공백 두 개로 강제 개행  
다음 줄입니다.

[^1]: 각주 본문입니다.
