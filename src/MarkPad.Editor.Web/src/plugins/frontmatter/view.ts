/**
 * Front matter NodeView (PRD F-VIEW-07 / F-EDIT-15): collapsed one-line summary; expanded tree editor built
 * from plain DOM. Every change rewrites the node's `raw` attr through the yaml Document API (model.ts).
 */
import type { Node as ProseNode } from '@milkdown/kit/prose/model'
import type { EditorView, NodeView } from '@milkdown/kit/prose/view'
import type { Document } from 'yaml'
import {
  addChildMap,
  addChildSeq,
  addKey,
  coerce,
  deleteKey,
  emptyDocument,
  formatScalar,
  moveKey,
  parseFrontMatter,
  renameKey,
  setSeqItems,
  setValue,
  summarize,
  type FmNode,
  type FmPath,
} from './model'

const expandedNodes = new WeakSet<object>()

export class FrontmatterNodeView implements NodeView {
  dom: HTMLElement
  private node: ProseNode
  private expanded = false
  private readonly header: HTMLElement
  private readonly body: HTMLElement
  private readonly summary: HTMLElement
  private readonly badge: HTMLElement
  private rawMode = false

  constructor(
    node: ProseNode,
    private readonly view: EditorView,
    private readonly getPos: () => number | undefined,
  ) {
    this.node = node
    this.dom = el('div', 'mp-fm')
    this.dom.contentEditable = 'false'
    this.header = el('div', 'mp-fm-header')
    const chevron = el('span', 'mp-fm-chevron', '▸')
    this.summary = el('span', 'mp-fm-summary')
    this.badge = el('span', 'mp-fm-badge', 'front matter')
    this.header.append(chevron, this.summary, this.badge)
    this.header.addEventListener('click', () => this.toggle())
    this.body = el('div', 'mp-fm-body')
    this.body.style.display = 'none'
    this.dom.append(this.header, this.body)
    this.expanded = expandedNodes.has(node)
    this.render()
  }

  update(node: ProseNode): boolean {
    if (node.type !== this.node.type) return false
    this.node = node
    this.render()
    return true
  }

  stopEvent(event: Event): boolean {
    return this.body.contains(event.target as Node)
  }

  ignoreMutation(): boolean {
    return true
  }

  selectNode(): void {
    this.dom.classList.add('mp-fm-selected')
  }

  deselectNode(): void {
    this.dom.classList.remove('mp-fm-selected')
  }

  private get raw(): string {
    return String(this.node.attrs['raw'] ?? '')
  }

  private toggle(): void {
    this.expanded = !this.expanded
    if (this.expanded) expandedNodes.add(this.node)
    else expandedNodes.delete(this.node)
    this.render()
  }

  private commit(raw: string): void {
    const pos = this.getPos()
    if (pos === undefined) return
    const next = raw.replace(/\n+$/, '')
    if (next === this.raw) return
    this.view.dispatch(this.view.state.tr.setNodeMarkup(pos, undefined, { ...this.node.attrs, raw: next }))
  }

  private render(): void {
    const parsed = parseFrontMatter(this.raw)
    this.summary.textContent = parsed.tree.length ? summarize(parsed.tree) : '(비어 있음)'
    this.header.querySelector('.mp-fm-chevron')!.textContent = this.expanded ? '▾' : '▸'
    this.badge.textContent = parsed.fallbackReason ? `raw · ${parsed.fallbackReason}` : 'front matter'
    this.badge.classList.toggle('mp-fm-warn', !!parsed.fallbackReason)
    this.body.style.display = this.expanded ? '' : 'none'
    if (!this.expanded) return
    this.body.replaceChildren()
    if (parsed.fallbackReason || this.rawMode) {
      this.renderRaw(parsed.fallbackReason)
      return
    }
    const doc = parsed.doc.contents == null ? emptyDocument() : parsed.doc
    this.renderTree(this.body, parsed.tree, [], doc)
    const foot = el('div', 'mp-fm-foot')
    const rawBtn = button('YAML 원문 편집', () => {
      this.rawMode = true
      this.render()
    })
    foot.append(this.addButtons(doc, []), rawBtn)
    this.body.append(foot)
  }

  private renderRaw(reason: string | null): void {
    if (reason) this.body.append(el('div', 'mp-fm-warning', `GUI로 편집할 수 없는 YAML입니다 (${reason}). 원문을 직접 편집합니다.`))
    const ta = document.createElement('textarea')
    ta.className = 'mp-fm-textarea'
    ta.value = this.raw
    ta.rows = Math.min(20, Math.max(4, this.raw.split('\n').length + 1))
    ta.addEventListener('change', () => this.commit(ta.value))
    ta.addEventListener('keydown', (e) => e.stopPropagation())
    this.body.append(ta)
    if (!reason) {
      this.body.append(
        button('트리 편집으로 돌아가기', () => {
          this.commit(ta.value)
          this.rawMode = false
          this.render()
        }),
      )
    }
  }

  private renderTree(container: HTMLElement, nodes: FmNode[], parentPath: FmPath, doc: Document): void {
    const list = el('div', 'mp-fm-rows')
    nodes.forEach((n, index) => list.append(this.renderRow(n, index, nodes.length, doc, parentPath)))
    container.append(list)
  }

  private renderRow(n: FmNode, index: number, count: number, doc: Document, parentPath: FmPath): HTMLElement {
    const row = el('div', `mp-fm-row mp-fm-${n.kind}`)
    const isSeqItem = typeof n.key === 'number'

    // key
    const keyBox = document.createElement('input')
    keyBox.className = 'mp-fm-key'
    keyBox.value = String(n.key)
    keyBox.readOnly = isSeqItem
    keyBox.addEventListener('change', () => {
      if (keyBox.value.trim() && keyBox.value !== n.key) this.commit(renameKey(doc, n.path, keyBox.value.trim()))
    })
    keyBox.addEventListener('keydown', (e) => e.stopPropagation())
    row.append(keyBox)

    // value
    const valueBox = el('div', 'mp-fm-value')
    if (n.kind === 'scalar') valueBox.append(this.scalarControl(n, doc))
    else if (n.kind === 'seq' && n.items) valueBox.append(this.chipsControl(n, doc))
    else valueBox.append(el('span', 'mp-fm-type', n.kind === 'map' ? '{…}' : '[…]'))
    row.append(valueBox)

    // actions
    const actions = el('div', 'mp-fm-actions')
    if (!isSeqItem) {
      actions.append(
        button('↑', () => this.commit(moveKey(doc, n.path, -1)), index === 0, '위로'),
        button('↓', () => this.commit(moveKey(doc, n.path, 1)), index === count - 1, '아래로'),
      )
    }
    actions.append(button('×', () => this.commit(deleteKey(doc, n.path)), false, '삭제'))
    row.append(actions)

    const wrapper = el('div', 'mp-fm-item')
    wrapper.append(row)
    if (n.children) {
      const sub = el('div', 'mp-fm-children')
      this.renderTree(sub, n.children, n.path, doc)
      if (n.kind === 'map') sub.append(this.addButtons(doc, n.path))
      wrapper.append(sub)
    }
    void parentPath
    return wrapper
  }

  private scalarControl(n: FmNode, doc: Document): HTMLElement {
    switch (n.scalarType) {
      case 'boolean': {
        const cb = document.createElement('input')
        cb.type = 'checkbox'
        cb.checked = n.value === true
        cb.addEventListener('change', () => this.commit(setValue(doc, n.path, cb.checked)))
        return cb
      }
      case 'date': {
        const d = document.createElement('input')
        d.type = String(n.value).length > 10 ? 'datetime-local' : 'date'
        d.value = n.value instanceof Date ? n.value.toISOString().slice(0, 10) : String(n.value).replace(' ', 'T')
        d.addEventListener('change', () => this.commit(setValue(doc, n.path, d.value.replace('T', ' '))))
        d.addEventListener('keydown', (e) => e.stopPropagation())
        return d
      }
      case 'number': {
        const num = document.createElement('input')
        num.type = 'number'
        num.step = 'any'
        num.value = String(n.value)
        num.addEventListener('change', () => this.commit(setValue(doc, n.path, num.value === '' ? null : Number(num.value))))
        num.addEventListener('keydown', (e) => e.stopPropagation())
        return num
      }
      default: {
        const text = document.createElement('input')
        text.type = 'text'
        text.value = formatScalar(n.value === null ? '' : n.value)
        text.placeholder = n.scalarType === 'null' ? 'null' : ''
        text.addEventListener('change', () => this.commit(setValue(doc, n.path, typeof n.value === 'string' ? text.value : coerce(text.value))))
        text.addEventListener('keydown', (e) => e.stopPropagation())
        return text
      }
    }
  }

  private chipsControl(n: FmNode, doc: Document): HTMLElement {
    const wrap = el('div', 'mp-fm-chips')
    const items = [...(n.items ?? [])]
    const rerender = (): void => this.commit(setSeqItems(doc, n.path, items))
    items.forEach((v, i) => {
      const chip = el('span', 'mp-fm-chip', formatScalar(v))
      const x = button('×', () => {
        items.splice(i, 1)
        rerender()
      }, false, '항목 제거')
      chip.append(x)
      wrap.append(chip)
    })
    const input = document.createElement('input')
    input.type = 'text'
    input.placeholder = '추가 후 Enter'
    input.className = 'mp-fm-chip-input'
    input.addEventListener('keydown', (e) => {
      e.stopPropagation()
      if (e.key === 'Enter' && input.value.trim()) {
        items.push(coerce(input.value))
        rerender()
      }
    })
    wrap.append(input)
    return wrap
  }

  private addButtons(doc: Document, parentPath: FmPath): HTMLElement {
    const bar = el('div', 'mp-fm-add')
    const ask = (): string | null => {
      const key = window.prompt('키 이름')
      return key && key.trim() ? key.trim() : null
    }
    bar.append(
      button('+ 키', () => {
        const k = ask()
        if (k) this.commit(addKey(doc, parentPath, k, ''))
      }),
      button('+ 객체', () => {
        const k = ask()
        if (k) this.commit(addChildMap(doc, parentPath, k))
      }),
      button('+ 목록', () => {
        const k = ask()
        if (k) this.commit(addChildSeq(doc, parentPath, k))
      }),
    )
    return bar
  }
}

function el(tag: string, className: string, text?: string): HTMLElement {
  const e = document.createElement(tag)
  e.className = className
  if (text !== undefined) e.textContent = text
  return e
}

function button(label: string, onClick: () => void, disabled = false, title?: string): HTMLButtonElement {
  const b = document.createElement('button')
  b.type = 'button'
  b.className = 'mp-fm-btn'
  b.textContent = label
  b.disabled = disabled
  if (title) b.title = title
  b.addEventListener('mousedown', (e) => e.preventDefault())
  b.addEventListener('click', (e) => {
    e.preventDefault()
    onClick()
  })
  return b
}
