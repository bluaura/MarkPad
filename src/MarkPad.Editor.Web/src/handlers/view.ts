import { getMarkdown } from '@milkdown/kit/utils'
import { closeBlockSource, showBlockSource } from '../block-source'
import { bridge } from '../bridge'
import type { EditorTheme } from '../bridge-types'
import * as cmd from '../commands'
import { renderHtml } from '../export/render-html'
import { collectOutline, gotoOutline } from '../plugins/outline'
import { getSession, requireSession } from '../session'
import { applyTheme, currentTheme } from '../theme'

export function registerViewHandlers(): void {
  bridge.register('view.setTheme', (raw) => {
    applyTheme((raw ?? {}) as Partial<EditorTheme>)
    return null
  })

  bridge.register('view.focus', () => {
    getSession()?.editor.focus()
    return null
  })

  bridge.register('view.scrollToTop', () => {
    window.scrollTo({ top: 0 })
    return null
  })

  // PRD F-VIEW-05
  bridge.register('outline.goto', (raw) => {
    const p = raw as { pos?: number } | undefined
    gotoOutline(requireSession().editor.view(), Number(p?.pos ?? 0))
    return null
  })

  // PRD F-EDIT-12
  bridge.register('block.showSource', (raw) => {
    const p = raw as { pos?: number } | undefined
    const s = requireSession()
    s.editor.action((ctx) => showBlockSource(ctx, typeof p?.pos === 'number' ? p.pos : undefined))
    return null
  })

  bridge.register('outline.refresh', () => {
    const s = requireSession()
    bridge.emit('outline', collectOutline(s.editor.view()))
    return null
  })

  bridge.register('block.closeSource', () => {
    closeBlockSource()
    return null
  })

  bridge.register('history.undo', () => {
    cmd.undo(requireSession().editor)
    return null
  })

  bridge.register('history.redo', () => {
    cmd.redo(requireSession().editor)
    return null
  })

  bridge.register('edit.selectAll', () => {
    cmd.selectAll(requireSession().editor)
    return null
  })

  // PRD F-EXP-04: selection (or whole document) as HTML + plain text for the host clipboard.
  bridge.register('edit.copyRich', async () => {
    const s = requireSession()
    const view = s.editor.view()
    const { from, to, empty } = view.state.selection
    const markdown = empty ? s.editor.getMarkdown() : s.editor.action(getMarkdown({ from, to }))
    const r = await renderHtml(markdown, { inlineImages: false, theme: currentTheme().mode })
    const text = empty ? view.state.doc.textBetween(0, view.state.doc.content.size, '\n') : view.state.doc.textBetween(from, to, '\n')
    return { html: r.html, text, markdown }
  })

  for (const op of ['cut', 'copy', 'paste'] as const) {
    bridge.register(`edit.${op}`, () => {
      requireSession().editor.focus()
      document.execCommand(op)
      return null
    })
  }
}
