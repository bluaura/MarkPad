/**
 * Inline/block HTML rendering (PRD Appendix C "렌더는 하되 편집은 소스 블록으로만", ARCHITECTURE §7.4):
 * Milkdown keeps raw HTML in `html` nodes; this NodeView shows them sanitized with DOMPurify (no scripts,
 * no event handlers) and stays non-editable — the source is changed through 블록 소스 편집 (F-EDIT-12).
 * mdast splits HTML at blank lines, so an unpaired fragment (e.g. a lone `</details>`) sanitizes to nothing;
 * those render as a small source chip so the user still sees where the tag is.
 */
import type { Ctx } from '@milkdown/kit/ctx'
import { htmlSchema } from '@milkdown/kit/preset/commonmark'
import type { Node as ProseNode } from '@milkdown/kit/prose/model'
import type { EditorView, NodeView } from '@milkdown/kit/prose/view'
import { $view } from '@milkdown/kit/utils'
import DOMPurify from 'dompurify'
import { showBlockSource } from '../block-source'

const BLOCK_TAG_RE = /^\s*<\/?(div|details|summary|table|thead|tbody|tr|td|th|p|ul|ol|li|section|article|aside|figure|figcaption|blockquote|pre|h[1-6]|hr|iframe|video|audio|center|dl|dt|dd|nav|header|footer|form)\b/i

export function sanitizeHtml(value: string): string {
  return DOMPurify.sanitize(value, {
    USE_PROFILES: { html: true, svg: true, mathMl: true },
    ADD_ATTR: ['target', 'width', 'height', 'align', 'open'],
    FORBID_TAGS: ['style', 'link', 'meta', 'base', 'object', 'embed'],
  })
}

export function isBlockHtml(value: string): boolean {
  return BLOCK_TAG_RE.test(value) || value.includes('\n')
}

export class HtmlNodeView implements NodeView {
  dom: HTMLElement
  private node: ProseNode

  constructor(
    node: ProseNode,
    private readonly view: EditorView,
    private readonly getPos: () => number | undefined,
    private readonly ctx: Ctx,
  ) {
    this.node = node
    const value = String(node.attrs['value'] ?? '')
    this.dom = document.createElement(isBlockHtml(value) ? 'div' : 'span')
    this.dom.className = 'mp-html'
    this.dom.contentEditable = 'false'
    this.render()
  }

  update(node: ProseNode): boolean {
    if (node.type !== this.node.type) return false
    this.node = node
    this.render()
    return true
  }

  ignoreMutation(): boolean {
    return true
  }

  stopEvent(event: Event): boolean {
    // Let clicks on the source chip through to us; everything else goes to ProseMirror (selection etc.).
    return (event.target as HTMLElement | null)?.classList.contains('mp-html-chip') ?? false
  }

  private render(): void {
    const value = String(this.node.attrs['value'] ?? '')
    this.dom.replaceChildren()
    this.dom.title = value.length > 200 ? `${value.slice(0, 200)}…` : value
    if (this.dom.tagName !== 'DIV') {
      // mdast splits inline HTML per tag (`<kbd>`, text, `</kbd>`), so a fragment cannot render as an element.
      // Show it as muted source text; the surrounding text stays editable.
      this.dom.classList.add('mp-html-inline')
      this.dom.textContent = value
      return
    }
    const clean = sanitizeHtml(value)
    const probe = document.createElement('div')
    probe.innerHTML = clean
    const hasVisibleContent = probe.textContent!.trim().length > 0 || probe.querySelector('img,svg,video,audio,iframe,hr,input,table') !== null
    if (hasVisibleContent) {
      const content = document.createElement(this.dom.tagName === 'DIV' ? 'div' : 'span')
      content.className = 'mp-html-content'
      content.innerHTML = clean
      this.dom.appendChild(content)
    }
    const chip = document.createElement('button')
    chip.type = 'button'
    chip.className = 'mp-html-chip'
    chip.textContent = hasVisibleContent ? '</>' : value.trim().split('\n')[0]!.slice(0, 40) || '</>'
    chip.title = '인라인 HTML — 클릭하여 소스 편집'
    chip.addEventListener('mousedown', (e) => e.preventDefault())
    chip.addEventListener('click', (e) => {
      e.preventDefault()
      const pos = this.getPos()
      if (pos !== undefined) showBlockSource(this.ctx, pos)
    })
    this.dom.appendChild(chip)
    void this.view
  }
}

export const htmlRenderView = $view(htmlSchema.node, (ctx) => (node, view, getPos) => new HtmlNodeView(node, view, getPos, ctx))
