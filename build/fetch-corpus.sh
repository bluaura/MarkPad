#!/usr/bin/env bash
# T-09: download public README.md files into corpus/roundtrip/ (original = expected, no .expected files).
# Sources and their licenses are listed in corpus/README.md. Re-runnable; skips files that already exist.
set -u
root="$(cd "$(dirname "$0")/.." && pwd)"
out="$root/corpus/roundtrip"
mkdir -p "$out"

# owner/repo[:path] — path defaults to README.md
repos=(
  microsoft/vscode microsoft/TypeScript microsoft/terminal microsoft/PowerToys microsoft/WindowsAppSDK
  microsoft/microsoft-ui-xaml microsoft/playwright microsoft/monaco-editor dotnet/runtime dotnet/aspnetcore
  dotnet/maui dotnet/roslyn PowerShell/PowerShell CommunityToolkit/dotnet CommunityToolkit/Windows
  facebook/react vuejs/core angular/angular sveltejs/svelte solidjs/solid
  nodejs/node denoland/deno oven-sh/bun vercel/next.js remix-run/remix
  expressjs/express nestjs/nest fastify/fastify koajs/koa hapijs/hapi
  prettier/prettier eslint/eslint webpack/webpack vitejs/vite rollup/rollup
  babel/babel axios/axios lodash/lodash mrdoob/three.js d3/d3
  chartjs/Chart.js Milkdown/milkdown ProseMirror/prosemirror remarkjs/remark markdown-it/markdown-it
  sindresorhus/awesome avelino/awesome-go vinta/awesome-python jwasham/coding-interview-university kamranahmedse/developer-roadmap
  donnemartin/system-design-primer trekhleb/javascript-algorithms TheAlgorithms/Python public-apis/public-apis ripienaar/free-for-dev
  EbookFoundation/free-programming-books getify/You-Dont-Know-JS airbnb/javascript google/styleguide gothinkster/realworld
  tldr-pages/tldr Homebrew/brew redis/redis postgres/postgres mongodb/mongo
  elastic/elasticsearch apache/spark apache/kafka grafana/grafana prometheus/prometheus
  curl/curl openssl/openssl FFmpeg/FFmpeg obsproject/obs-studio godotengine/godot
  golang/go rust-lang/rust python/cpython kubernetes/kubernetes docker/compose
  ansible/ansible hashicorp/terraform tensorflow/tensorflow pytorch/pytorch huggingface/transformers
  scikit-learn/scikit-learn pandas-dev/pandas numpy/numpy ohmyzsh/ohmyzsh neovim/neovim
  tmux/tmux git/git electron/electron flutter/flutter JetBrains/kotlin
  apple/swift KaTeX/KaTeX mathjax/MathJax 3b1b/manim mermaid-js/mermaid
  shikijs/shiki codemirror/dev jsdom/jsdom vitest-dev/vitest yaml/yaml
  # Files with YAML front matter (docs sites)
  "github/docs:content/get-started/start-your-journey/hello-world.md"
  "github/docs:content/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax.md"
  "jekyll/jekyll:docs/_docs/front-matter.md"
  "jekyll/jekyll:docs/_docs/step-by-step/01-setup.md"
  "gohugoio/hugoDocs:content/en/content-management/front-matter.md"
  "gohugoio/hugoDocs:content/en/getting-started/quick-start.md"
  "MicrosoftDocs/windows-dev-docs:hub/apps/windows-app-sdk/index.md"
  "MicrosoftDocs/windows-dev-docs:hub/apps/winui/winui3/index.md"
  "MicrosoftDocs/windows-dev-docs:hub/apps/develop/data-access/sqlite-data-access.md"
  "MicrosoftDocs/windows-dev-docs:hub/apps/design/style/typography.md"
  "microsoft/vscode-docs:docs/editor/codebasics.md"
  "microsoft/vscode-docs:docs/languages/markdown.md"
  "Milkdown/milkdown:docs/guide/getting-started.md"
  "remarkjs/remark:doc/plugins.md"
)

ok=0; fail=0
for entry in "${repos[@]}"; do
  repo="${entry%%:*}"
  path="README.md"; [[ "$entry" == *:* ]] && path="${entry#*:}"
  name="$(echo "${repo}-${path}" | sed 's#[/:]#-#g' | sed 's#README\.md$#README.md#')"
  name="${name//--/-}"
  target="$out/$name"
  if [[ -s "$target" ]]; then ok=$((ok+1)); continue; fi
  got=""
  for branch in main master; do
    url="https://raw.githubusercontent.com/$repo/$branch/$path"
    if curl -fsSL --max-time 30 "$url" -o "$target.tmp" 2>/dev/null && [[ -s "$target.tmp" ]]; then got="$branch"; break; fi
  done
  if [[ -n "$got" ]]; then
    mv "$target.tmp" "$target"; ok=$((ok+1)); echo "ok   $repo ($got) $path"
  else
    rm -f "$target.tmp"; fail=$((fail+1)); echo "FAIL $repo $path"
  fi
done
echo "downloaded/present: $ok, failed: $fail"
