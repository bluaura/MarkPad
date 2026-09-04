/** Inline HTML rendering: sanitized display, scripts stripped, markdown source untouched. */
import { editorViewCtx } from '@milkdown/kit/core'
import { replaceAll } from '@milkdown/kit/utils'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { isBlockHtml, sanitizeHtml } from '../src/plugins/html-render'
import { createHeadlessEditor, type HeadlessEditor } from './helpers/headless-editor'

let editor: HeadlessEditor

beforeAll(async () => {
  editor = await createHeadlessEditor()
})

afterAll(async () => {
  await editor.destroy()
})

describe('inline HTML rendering', () => {
  it('sanitizes scripts and event handlers, keeps structure', () => {
    const clean = sanitizeHtml('<details open><summary onclick="x()">S</summary><script>alert(1)</script><b>bold</b></details>')
    expect(clean).toContain('<summary>S</summary>')
    expect(clean).toContain('<b>bold</b>')
    expect(clean).not.toContain('script')
    expect(clean).not.toContain('onclick')
  })

  it('classifies block vs inline html', () => {
    expect(isBlockHtml('<details>')).toBe(true)
    expect(isBlockHtml('<kbd>Ctrl</kbd>')).toBe(false)
    expect(isBlockHtml('<span>\nx</span>')).toBe(true)
  })

  it('renders html nodes through the NodeView and serializes unchanged', () => {
    const md = 'Press <kbd>Ctrl</kbd>+<kbd>B</kbd>.\n\n<details>\n<summary>More</summary>\n\nbody\n\n</details>\n'
    editor.crepe.editor.action(replaceAll(md, true))
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    const rendered = view.dom.querySelectorAll('.mp-html')
    expect(rendered.length).toBeGreaterThanOrEqual(3)
    // inline fragments (mdast splits per tag) stay as muted source; block html renders sanitized
    const inline = Array.from(view.dom.querySelectorAll('.mp-html-inline')).map((e) => e.textContent)
    expect(inline).toContain('<kbd>')
    expect(view.dom.querySelector('.mp-html-content summary')?.textContent).toBe('More')
    // the lone closing tag has no visible content → chip shows its source
    const chips = Array.from(view.dom.querySelectorAll('.mp-html-chip')).map((c) => c.textContent)
    expect(chips).toContain('</details>')
    expect(editor.crepe.getMarkdown()).toBe(md)
  })
})
