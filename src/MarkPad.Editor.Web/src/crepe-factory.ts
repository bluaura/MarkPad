import { Crepe } from '@milkdown/crepe'
import { remarkStringifyOptionsCtx } from '@milkdown/kit/core'
import { remarkPreserveEmptyLinePlugin } from '@milkdown/kit/preset/commonmark'
import type { EditorSettings } from './bridge-types'
import { markPadCodeExtensions, markPadCodeTheme } from './codemirror-theme'
import { codeMetaPlugin } from './plugins/code-meta'
import { contextBarPlugin } from './plugins/context-bar'
import { findPlugin } from './plugins/find'
import { formatKeymap } from './plugins/format-keymap'
import { frontmatterPlugin } from './plugins/frontmatter'
import { highlightPlugin, highlightStringifyHandler } from './plugins/highlight'
import { htmlRenderView } from './plugins/html-render'
import { outlinePlugin } from './plugins/outline'
import { imageAltPlugin } from './plugins/image-alt'
import { imagePropsPlugin } from './plugins/image-props'
import { linkClickPlugin } from './plugins/link-click'

let mermaidSeq = 0
let mermaidTimer = 0

function renderMermaidPreview(language: string, content: string, apply: (value: null | string | HTMLElement) => void): void | null {
  if (language !== 'mermaid') return null
  if (!content.trim()) {
    apply(null)
    return
  }
  window.clearTimeout(mermaidTimer)
  mermaidTimer = window.setTimeout(async () => {
    try {
      const mermaid = (await import('mermaid')).default
      mermaid.initialize({ startOnLoad: false, theme: document.documentElement.dataset['theme'] === 'dark' ? 'dark' : 'default', securityLevel: 'strict' })
      const { svg } = await mermaid.render(`mp-mermaid-${++mermaidSeq}`, content)
      const holder = document.createElement('div')
      holder.className = 'mp-mermaid-preview'
      holder.innerHTML = svg
      apply(holder)
    } catch (err) {
      const pre = document.createElement('pre')
      pre.className = 'mp-mermaid-error'
      pre.textContent = String(err instanceof Error ? err.message : err)
      apply(pre)
    }
  }, 300)
}

export interface CrepeFactoryOptions {
  /** Headless (vitest/jsdom): skip features that need layout or a host. */
  headless?: boolean
  onUpload?: (file: File) => Promise<string>
  proxyDomURL?: (url: string) => Promise<string> | string
}

/**
 * Single place that assembles the Crepe editor (ARCHITECTURE §3.3 "부트스트랩") so the app and the
 * round-trip corpus tests exercise the exact same schema and serializer options.
 */
export function buildCrepe(root: HTMLElement, defaultValue: string, settings: EditorSettings, opts: CrepeFactoryOptions = {}): Crepe {
  const headless = opts.headless === true
  const crepe = new Crepe({
    root,
    defaultValue,
    features: {
      [Crepe.Feature.Toolbar]: false, // native XAML toolbar (ADR-02)
      [Crepe.Feature.TopBar]: false,
      [Crepe.Feature.BlockEdit]: false, // revisit in P2 (T-61)
      [Crepe.Feature.AI]: false,
      [Crepe.Feature.Placeholder]: !headless,
      [Crepe.Feature.Cursor]: !headless,
      [Crepe.Feature.LinkTooltip]: !headless,
      [Crepe.Feature.Latex]: settings.markdown.extMath,
    },
    featureConfigs: {
      [Crepe.Feature.ImageBlock]: {
        ...(opts.onUpload ? { onUpload: opts.onUpload, inlineOnUpload: opts.onUpload, blockOnUpload: opts.onUpload } : {}),
        ...(opts.proxyDomURL ? { proxyDomURL: opts.proxyDomURL } : {}),
        blockUploadPlaceholderText: '이미지를 붙여넣거나 끌어다 놓으세요',
        blockCaptionPlaceholderText: '캡션',
        inlineUploadPlaceholderText: '이미지 URL 또는 파일',
      },
      [Crepe.Feature.Placeholder]: {
        text: '내용을 입력하세요…',
        mode: 'doc',
      },
      [Crepe.Feature.CodeMirror]: {
        // CSS-variable theme so code blocks follow light/dark (T-33); languages: Crepe default (128, lazy).
        theme: markPadCodeTheme,
        extensions: markPadCodeExtensions,
        searchPlaceholder: '언어 검색',
        noResultText: '결과 없음',
        copyText: '복사',
        previewLabel: '미리보기',
        // PRD F-VIEW-06 / T-47: mermaid preview under the code (dynamic import keeps the main bundle small).
        renderPreview: settings.markdown.extMermaid && !headless ? renderMermaidPreview : () => null,
      },
      [Crepe.Feature.LinkTooltip]: {
        inputPlaceholder: '링크 주소 입력…',
      },
    },
  })

  // Without this remark plugin empty paragraphs/cells serialize as nothing instead of `<br />` (round-trip noise).
  crepe.editor.remove(remarkPreserveEmptyLinePlugin)
  crepe.editor.use(imageAltPlugin)
  crepe.editor.use(codeMetaPlugin)
  crepe.editor.use(formatKeymap)
  crepe.editor.use(findPlugin)
  if (settings.markdown.frontMatter) crepe.editor.use(frontmatterPlugin)
  if (settings.markdown.extHighlight) crepe.editor.use(highlightPlugin)
  crepe.editor.use(htmlRenderView)
  if (!headless) {
    crepe.editor.use(linkClickPlugin)
    crepe.editor.use(contextBarPlugin)
    crepe.editor.use(outlinePlugin)
    crepe.editor.use(imagePropsPlugin)
  }

  // Serialization style for NEW/changed blocks (PRD F-SET-04). Untouched blocks keep the original (§5).
  crepe.editor.config((ctx) => {
    ctx.update(remarkStringifyOptionsCtx, (prev) => ({
      ...prev,
      bullet: settings.markdown.bullet,
      emphasis: settings.markdown.emphasis,
      strong: '*' as const,
      listItemIndent: settings.markdown.listIndent >= 4 ? ('tab' as const) : ('one' as const),
      fences: true,
      rule: '-' as const,
      ruleRepetition: 3,
      ...(settings.markdown.extHighlight
        ? { handlers: { ...(prev.handlers ?? {}), highlight: highlightStringifyHandler as never } }
        : {}),
    }))
  })

  return crepe
}

export const defaultEditorSettings: EditorSettings = {
  theme: {
    mode: 'light',
    fontFamily: "'Segoe UI Variable Text', 'Segoe UI', 'Malgun Gothic', sans-serif",
    fontSize: 15,
    lineHeight: 1.7,
    maxWidth: 800,
    zoom: 1,
  },
  markdown: {
    emphasis: '*',
    bullet: '-',
    listIndent: 2,
    extHighlight: false,
    extMath: true,
    extMermaid: true,
    frontMatter: true,
  },
  allowRemoteImages: true,
}
