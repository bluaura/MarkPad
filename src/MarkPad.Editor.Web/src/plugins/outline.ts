import { Plugin, PluginKey, TextSelection } from '@milkdown/kit/prose/state'
import type { EditorView } from '@milkdown/kit/prose/view'
import { $prose } from '@milkdown/kit/utils'
import { bridge } from '../bridge'
import type { OutlineItem } from '../bridge-types'

/** PRD F-VIEW-05 / T-45: heading tree for the outline pane, 200ms debounced, with the active heading index. */

export interface OutlinePayload {
  items: OutlineItem[]
  active: number
}

export function collectOutline(view: EditorView): OutlinePayload {
  const items: OutlineItem[] = []
  view.state.doc.forEach((node, offset) => {
    if (node.type.name === 'heading') {
      items.push({ level: Number(node.attrs['level'] ?? 1), text: node.textContent, pos: offset })
    }
  })
  const cursor = view.state.selection.from
  let active = -1
  for (let i = 0; i < items.length; i++) {
    if (items[i]!.pos <= cursor) active = i
    else break
  }
  return { items, active }
}

export const outlinePlugin = $prose(() => {
  let timer = 0
  let last = ''
  return new Plugin({
    key: new PluginKey('markpad-outline'),
    view: (view) => {
      const emit = (): void => {
        const payload = collectOutline(view)
        const key = JSON.stringify(payload)
        if (key === last) return
        last = key
        bridge.emit('outline', payload)
      }
      emit()
      return {
        update: () => {
          window.clearTimeout(timer)
          timer = window.setTimeout(emit, 200)
        },
        destroy: () => window.clearTimeout(timer),
      }
    },
  })
})

export function gotoOutline(view: EditorView, pos: number): void {
  const node = view.state.doc.nodeAt(pos)
  if (!node) return
  const inside = Math.min(pos + 1, view.state.doc.content.size)
  view.dispatch(view.state.tr.setSelection(TextSelection.create(view.state.doc, inside)).scrollIntoView())
  view.focus()
  const dom = view.nodeDOM(pos) as HTMLElement | null
  dom?.scrollIntoView({ block: 'start', behavior: 'smooth' })
}
