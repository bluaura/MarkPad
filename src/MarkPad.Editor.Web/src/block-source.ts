import { parserCtx, serializerCtx, editorViewCtx } from '@milkdown/kit/core'
import type { Ctx } from '@milkdown/kit/ctx'
import { Fragment } from '@milkdown/kit/prose/model'
import { TextSelection } from '@milkdown/kit/prose/state'
import { BridgeError } from './bridge'

/**
 * PRD F-EDIT-12 / T-55: show the markdown source of one top-level block in an inline textarea and re-parse it
 * on apply. No whole-document source mode by design.
 */
let panel: HTMLElement | null = null

export function closeBlockSource(): void {
  panel?.remove()
  panel = null
}

export function showBlockSource(ctx: Ctx, at?: number): void {
  closeBlockSource()
  const view = ctx.get(editorViewCtx)
  const state = view.state
  const pos = at ?? state.selection.from
  const $pos = state.doc.resolve(Math.min(pos, state.doc.content.size))
  const index = $pos.depth === 0 ? Math.min($pos.index(0), state.doc.childCount - 1) : $pos.index(0)
  const block = state.doc.maybeChild(index)
  if (!block) throw new BridgeError('BAD_PARAM', 'no block at position')
  const blockPos = $pos.depth === 0 ? $pos.posAtIndex(index, 0) : $pos.before(1)

  const serialize = ctx.get(serializerCtx)
  const parse = ctx.get(parserCtx)
  const single = state.doc.type.create(null, Fragment.from(block))
  const source = serialize(single).replace(/\n+$/, '')

  const dom = view.nodeDOM(blockPos) as HTMLElement | null
  const root = view.dom.closest('.markpad-editor') ?? document.body
  panel = document.createElement('div')
  panel.className = 'mp-source-panel'
  panel.addEventListener('mousedown', (e) => e.stopPropagation())

  const title = document.createElement('div')
  title.className = 'mp-source-title'
  title.textContent = `블록 소스 (${block.type.name})`
  const ta = document.createElement('textarea')
  ta.className = 'mp-source-text'
  ta.value = source
  ta.rows = Math.min(24, Math.max(4, source.split('\n').length + 1))
  ta.addEventListener('keydown', (e) => {
    e.stopPropagation()
    if (e.key === 'Escape') closeBlockSource()
    if (e.key === 'Enter' && e.ctrlKey) apply()
  })
  const actions = document.createElement('div')
  actions.className = 'mp-source-actions'
  const applyBtn = document.createElement('button')
  applyBtn.type = 'button'
  applyBtn.textContent = '적용 (Ctrl+Enter)'
  const cancelBtn = document.createElement('button')
  cancelBtn.type = 'button'
  cancelBtn.textContent = '취소 (Esc)'
  actions.append(applyBtn, cancelBtn)
  panel.append(title, ta, actions)

  const apply = (): void => {
    const parsed = parse(ta.value)
    if (!parsed) return
    const current = view.state.doc.maybeChild(index)
    if (!current) return
    const from = view.state.doc.resolve(0).posAtIndex(index, 0)
    const to = from + current.nodeSize
    const tr = view.state.tr.replaceWith(from, to, parsed.content)
    const sel = Math.min(from + 1, tr.doc.content.size)
    view.dispatch(tr.setSelection(TextSelection.near(tr.doc.resolve(sel))).scrollIntoView())
    closeBlockSource()
    view.focus()
  }
  applyBtn.addEventListener('click', apply)
  cancelBtn.addEventListener('click', () => {
    closeBlockSource()
    view.focus()
  })

  root.appendChild(panel)
  const rootRect = (panel.offsetParent ?? root).getBoundingClientRect()
  const rect = dom?.getBoundingClientRect()
  if (rect) {
    panel.style.left = `${Math.max(8, rect.left - rootRect.left)}px`
    panel.style.top = `${rect.bottom - rootRect.top + 6}px`
    panel.style.width = `${Math.max(320, rect.width)}px`
  }
  ta.focus()
}
