import { bridge } from '../bridge'
import type { EditorTheme } from '../bridge-types'
import * as cmd from '../commands'
import { getSession, requireSession } from '../session'
import { applyTheme } from '../theme'

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

  for (const op of ['cut', 'copy', 'paste'] as const) {
    bridge.register(`edit.${op}`, () => {
      requireSession().editor.focus()
      document.execCommand(op)
      return null
    })
  }
}
