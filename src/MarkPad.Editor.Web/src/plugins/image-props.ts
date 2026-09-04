import type { Node as ProseNode } from '@milkdown/kit/prose/model'
import { Plugin, PluginKey } from '@milkdown/kit/prose/state'
import type { EditorView } from '@milkdown/kit/prose/view'
import { $prose } from '@milkdown/kit/utils'

/**
 * PRD F-IMG-05 / T-58b: click an image → popover to edit alt text and path, or delete the node.
 * Works for inline `image` and Crepe `image-block` nodes; the file on disk is never touched.
 */
const IMAGE_TYPES = new Set(['image', 'image-block'])

let panel: HTMLElement | null = null

function closePanel(): void {
  panel?.remove()
  panel = null
}

function findImageNode(view: EditorView, img: HTMLElement): { node: ProseNode; pos: number } | null {
  let pos: number
  try {
    pos = view.posAtDOM(img, 0)
  } catch {
    return null
  }
  const $p = view.state.doc.resolve(Math.min(pos, view.state.doc.content.size))
  if ($p.nodeAfter && IMAGE_TYPES.has($p.nodeAfter.type.name)) return { node: $p.nodeAfter, pos }
  if ($p.nodeBefore && IMAGE_TYPES.has($p.nodeBefore.type.name)) return { node: $p.nodeBefore, pos: pos - $p.nodeBefore.nodeSize }
  for (let d = $p.depth; d > 0; d--) {
    const n = $p.node(d)
    if (IMAGE_TYPES.has(n.type.name)) return { node: n, pos: $p.before(d) }
  }
  // Fallback: the nearest image node around the position (Crepe's block view can map to the block boundary).
  let found: { node: ProseNode; pos: number } | null = null
  view.state.doc.nodesBetween(Math.max(0, pos - 1), Math.min(view.state.doc.content.size, pos + 1), (n, p) => {
    if (!found && IMAGE_TYPES.has(n.type.name)) found = { node: n, pos: p }
    return !found
  })
  return found
}

function field(label: string, value: string): { wrap: HTMLElement; input: HTMLInputElement } {
  const wrap = document.createElement('label')
  wrap.className = 'mp-img-field'
  const span = document.createElement('span')
  span.textContent = label
  const input = document.createElement('input')
  input.type = 'text'
  input.value = value
  input.addEventListener('keydown', (e) => e.stopPropagation())
  wrap.append(span, input)
  return { wrap, input }
}

export function showImageProps(view: EditorView, img: HTMLElement): boolean {
  const hit = findImageNode(view, img)
  if (!hit) return false
  closePanel()
  const { node, pos } = hit
  const root = view.dom.closest('.markpad-editor') ?? document.body
  panel = document.createElement('div')
  panel.className = 'mp-img-panel'
  panel.addEventListener('mousedown', (e) => e.stopPropagation())

  const alt = field('대체 텍스트 (alt)', String(node.attrs['alt'] ?? ''))
  const src = field('경로 / URL', String(node.attrs['src'] ?? ''))
  const actions = document.createElement('div')
  actions.className = 'mp-img-actions'
  const apply = document.createElement('button')
  apply.type = 'button'
  apply.textContent = '적용'
  const remove = document.createElement('button')
  remove.type = 'button'
  remove.textContent = '이미지 삭제'
  remove.className = 'mp-img-danger'
  const cancel = document.createElement('button')
  cancel.type = 'button'
  cancel.textContent = '닫기'
  actions.append(apply, remove, cancel)
  panel.append(alt.wrap, src.wrap, actions)

  const current = (): { node: ProseNode; pos: number } | null => {
    const n = view.state.doc.nodeAt(pos)
    return n && IMAGE_TYPES.has(n.type.name) ? { node: n, pos } : findImageNode(view, img)
  }
  apply.addEventListener('click', () => {
    const c = current()
    if (c) {
      view.dispatch(view.state.tr.setNodeMarkup(c.pos, undefined, { ...c.node.attrs, alt: alt.input.value, src: src.input.value.trim() }))
    }
    closePanel()
    view.focus()
  })
  remove.addEventListener('click', () => {
    const c = current()
    if (c) view.dispatch(view.state.tr.delete(c.pos, c.pos + c.node.nodeSize).scrollIntoView())
    closePanel()
    view.focus()
  })
  cancel.addEventListener('click', () => {
    closePanel()
    view.focus()
  })
  for (const i of [alt.input, src.input]) {
    i.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') apply.click()
      if (e.key === 'Escape') cancel.click()
    })
  }

  root.appendChild(panel)
  const rootRect = (panel.offsetParent ?? root).getBoundingClientRect()
  const rect = img.getBoundingClientRect()
  panel.style.left = `${Math.max(8, rect.left - rootRect.left)}px`
  panel.style.top = `${rect.bottom - rootRect.top + 6}px`
  alt.input.focus()
  alt.input.select()
  return true
}

export const imagePropsPlugin = $prose(() => {
  return new Plugin({
    key: new PluginKey('markpad-image-props'),
    props: {
      handleDOMEvents: {
        click(view, event) {
          const target = event.target as HTMLElement | null
          if (!target) return false
          if (panel && !panel.contains(target)) closePanel()
          if (target.tagName !== 'IMG') return false
          if (view.props.editable && !view.props.editable(view.state)) return false
          return showImageProps(view, target)
        },
      },
    },
    view: () => ({ destroy: closePanel }),
  })
})
