/**
 * Static HTML export (PRD F-EXP-01, T-40): markdown → HTML with the same code colors as the editor
 * (Lezer highlighters, CSS variables), KaTeX math, Mermaid diagrams as inline SVG, optional base64 images.
 */
import { languages } from '@codemirror/language-data'
import { classHighlighter, highlightCode } from '@lezer/highlight'
import type { Element, Root as HastRoot, Text as HastText } from 'hast'
import rehypeKatex from 'rehype-katex'
import rehypeRaw from 'rehype-raw'
import rehypeStringify from 'rehype-stringify'
import remarkFrontmatter from 'remark-frontmatter'
import remarkGfm from 'remark-gfm'
import remarkMath from 'remark-math'
import remarkParse from 'remark-parse'
import remarkRehype from 'remark-rehype'
import { unified } from 'unified'
import { visit } from 'unist-util-visit'
import { bridge } from '../bridge'
import exportCss from './export.css?inline'

export interface RenderHtmlOptions {
  inlineImages: boolean
  theme: 'light' | 'dark'
  /** Resolve local image src to a data URL (host reads the file). Omit in tests. */
  readImage?: (src: string) => Promise<string | null>
}

export interface RenderHtmlResult {
  html: string
  css: string
  hasMath: boolean
}

const REMOTE_RE = /^(https?:)?\/\//i
const DATA_RE = /^data:/i

function textOf(el: Element): string {
  let out = ''
  for (const child of el.children) {
    if (child.type === 'text') out += (child as HastText).value
    else if (child.type === 'element') out += textOf(child as Element)
  }
  return out
}

function langOf(code: Element): string | null {
  const cls = code.properties?.['className']
  const list = Array.isArray(cls) ? cls.map(String) : typeof cls === 'string' ? [cls] : []
  const m = list.map((c) => /^language-(.+)$/.exec(c)?.[1]).find(Boolean)
  return m ?? null
}

async function highlightElement(code: Element, lang: string): Promise<void> {
  const desc = languages.find((l) => l.name.toLowerCase() === lang.toLowerCase() || l.alias.includes(lang.toLowerCase()) || l.extensions.includes(lang.toLowerCase()))
  if (!desc) return
  const language = await desc.load()
  const source = textOf(code)
  const tree = language.language.parser.parse(source)
  const children: (Element | HastText)[] = []
  highlightCode(
    source,
    tree,
    classHighlighter,
    (text, classes) => {
      if (classes) children.push({ type: 'element', tagName: 'span', properties: { className: classes.split(' ') }, children: [{ type: 'text', value: text }] })
      else children.push({ type: 'text', value: text })
    },
    () => children.push({ type: 'text', value: '\n' }),
  )
  code.children = children
}

async function renderMermaid(code: string, theme: 'light' | 'dark'): Promise<string | null> {
  try {
    const mermaid = (await import('mermaid')).default
    mermaid.initialize({ startOnLoad: false, theme: theme === 'dark' ? 'dark' : 'default', securityLevel: 'strict' })
    const id = `mp-mermaid-${Math.random().toString(36).slice(2)}`
    const { svg } = await mermaid.render(id, code)
    return svg
  } catch (err) {
    bridge.log('warn', `mermaid export failed: ${String(err)}`)
    return null
  }
}

export async function renderHtml(markdown: string, options: RenderHtmlOptions): Promise<RenderHtmlResult> {
  const processor = unified()
    .use(remarkParse)
    .use(remarkGfm)
    .use(remarkFrontmatter, ['yaml'])
    .use(remarkMath)
    .use(remarkRehype, { allowDangerousHtml: true })
    .use(rehypeRaw)
    .use(rehypeKatex)

  const tree = (await processor.run(processor.parse(markdown))) as HastRoot
  let hasMath = false

  const tasks: Promise<void>[] = []
  visit(tree, 'element', (node: Element, index, parent) => {
    if (node.tagName === 'pre' && node.children[0]?.type === 'element' && (node.children[0] as Element).tagName === 'code') {
      const code = node.children[0] as Element
      const lang = langOf(code)
      if (lang === 'mermaid' && parent && typeof index === 'number') {
        const source = textOf(code)
        const holder = parent.children
        tasks.push(
          renderMermaid(source, options.theme).then((svg) => {
            if (!svg) return
            holder[index] = { type: 'element', tagName: 'div', properties: { className: ['mermaid'] }, children: [{ type: 'raw', value: svg } as unknown as HastText] }
          }),
        )
      } else if (lang) {
        tasks.push(highlightElement(code, lang))
      }
    }
    if (node.tagName === 'img' && options.inlineImages && options.readImage) {
      const src = String(node.properties?.['src'] ?? '')
      if (src && !REMOTE_RE.test(src) && !DATA_RE.test(src)) {
        tasks.push(
          options.readImage(src).then((dataUrl) => {
            if (dataUrl && node.properties) node.properties['src'] = dataUrl
          }),
        )
      }
    }
    const cls = node.properties?.['className']
    if (Array.isArray(cls) && cls.includes('katex')) hasMath = true
  })
  await Promise.all(tasks)

  const html = unified().use(rehypeStringify, { allowDangerousHtml: true }).stringify(tree)
  return { html, css: exportCss, hasMath }
}
