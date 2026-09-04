# MarkPad 테스트 코퍼스

## roundtrip/

원본 보존(round-trip) 검증용 Markdown 파일. 각 파일은 **원본 = 기대값**이며 별도의 `.expected` 파일은 없다
(IMPLEMENTATION-PLAN T-09). `build/fetch-corpus.sh`가 GitHub raw URL에서 내려받고, 파일명은
`{owner}-{repo}-{path}.md` 규칙을 따른다.

검증: `cd src/MarkPad.Editor.Web && npm test` → `test/milkdown-roundtrip.spec.ts`가 파일마다
Crepe(jsdom)로 파싱·직렬화 후 `roundtrip.ts`를 거쳐 바이트 동일 여부를 확인하고
`docs/perf/roundtrip-report.md`에 결과를 기록한다. 불일치 파일은 `docs/perf/roundtrip-diff/`에 남는다.

### 출처와 라이선스

파일은 각 저장소의 라이선스를 그대로 따른다. 이 코퍼스는 테스트 목적으로만 사용하며 재배포하지 않는다.

| 출처 | 라이선스 |
|---|---|
| microsoft/*, dotnet/*, PowerShell/*, CommunityToolkit/*, MicrosoftDocs/*, microsoft/vscode-docs | MIT / CC BY 4.0 (docs) |
| facebook/react, vuejs/core, angular/angular, sveltejs/svelte, solidjs/solid | MIT |
| nodejs/node, denoland/deno, oven-sh/bun, vercel/next.js, remix-run/remix | MIT |
| expressjs/express, nestjs/nest, fastify/fastify, koajs/koa, hapijs/hapi | MIT / BSD-3 |
| prettier/prettier, eslint/eslint, webpack/webpack, vitejs/vite, rollup/rollup, babel/babel, axios/axios, lodash/lodash | MIT |
| mrdoob/three.js, d3/d3, chartjs/Chart.js, Milkdown/milkdown, ProseMirror/prosemirror, remarkjs/remark, markdown-it/markdown-it | MIT / ISC / BSD |
| sindresorhus/awesome, avelino/awesome-go, vinta/awesome-python, kamranahmedse/developer-roadmap, public-apis/public-apis, ripienaar/free-for-dev | CC0 / MIT / CC BY 4.0 |
| jwasham/coding-interview-university, donnemartin/system-design-primer, trekhleb/javascript-algorithms, TheAlgorithms/Python, EbookFoundation/free-programming-books, getify/You-Dont-Know-JS, airbnb/javascript, google/styleguide, gothinkster/realworld, tldr-pages/tldr | CC BY-SA 4.0 / CC BY 4.0 / MIT / Apache-2.0 |
| Homebrew/brew, redis/redis, postgres/postgres, mongodb/mongo, elastic/elasticsearch, apache/spark, apache/kafka, grafana/grafana, prometheus/prometheus | BSD-2 / RSALv2 / PostgreSQL / SSPL / Apache-2.0 / AGPL-3.0 |
| curl/curl, openssl/openssl, FFmpeg/FFmpeg, obsproject/obs-studio, godotengine/godot | curl / Apache-2.0 / LGPL / GPL-2.0 / MIT |
| golang/go, rust-lang/rust, python/cpython, kubernetes/kubernetes, docker/compose, ansible/ansible, hashicorp/terraform | BSD-3 / MIT+Apache / PSF / Apache-2.0 / GPL-3.0 / BUSL |
| tensorflow/tensorflow, pytorch/pytorch, huggingface/transformers, scikit-learn/scikit-learn, pandas-dev/pandas, numpy/numpy | Apache-2.0 / BSD-3 |
| ohmyzsh/ohmyzsh, neovim/neovim, tmux/tmux, git/git, electron/electron, flutter/flutter, JetBrains/kotlin, apple/swift | MIT / Apache-2.0 / ISC / GPL-2.0 / BSD-3 |
| KaTeX/KaTeX, mathjax/MathJax, 3b1b/manim, mermaid-js/mermaid, shikijs/shiki, codemirror/dev, jsdom/jsdom, vitest-dev/vitest, yaml/yaml | MIT / Apache-2.0 / ISC |
| github/docs, jekyll/jekyll, gohugoio/hugoDocs | CC BY 4.0 / MIT / Apache-2.0 |

### 개인 문서 (본인 md 50개)

PRD §5.3의 "본인 md 50개(민감 정보 제거)"는 사용자가 직접 선별해 `corpus/roundtrip/personal-*.md`로
추가한다. 현재는 이 저장소의 `docs/*.md` 3개(PRD·ARCHITECTURE·IMPLEMENTATION-PLAN)를 복사해
`personal-markpad-*.md`로 포함했다.

## fixtures/

인코딩·EOL·front matter 케이스 (`tests/MarkPad.Core.Tests`에서 사용). PowerShell로 생성:
`utf8-lf`, `utf8-crlf`, `utf8-bom-lf`, `utf8-bom-crlf`, `utf8-lf-no-trailing-newline`, `mixed-eol`,
`frontmatter`, `cp949`, `plain.txt`.
