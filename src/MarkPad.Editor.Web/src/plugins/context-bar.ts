import { editorViewCtx } from '@milkdown/kit/core'
import type { Ctx } from '@milkdown/kit/ctx'
import type { Node as ProseNode } from '@milkdown/kit/prose/model'
import { Plugin, PluginKey } from '@milkdown/kit/prose/state'
import type { EditorView } from '@milkdown/kit/prose/view'
import { $prose } from '@milkdown/kit/utils'
import * as cmd from '../commands'
import { targetFromCtx } from './format-keymap'

/**
 * Context toolbars for tables and code blocks (PRD 4.2 "컨텍스트 툴바", ADR-07): rendered as plain DOM inside
 * the editor root (XAML cannot overlay WebView2), positioned above the block the cursor is in.
 */

interface BarButton {
  label: string
  title: string
  run: () => void
}

interface Target {
  kind: 'table' | 'code'
  node: ProseNode
  pos: number
}

function findTarget(view: EditorView): Target | null {
  const { $from } = view.state.selection
  for (let depth = $from.depth; depth > 0; depth--) {
    const node = $from.node(depth)
    if (node.type.name === 'table') return { kind: 'table', node, pos: $from.before(depth) }
    if (node.type.name === 'code_block') return { kind: 'code', node, pos: $from.before(depth) }
  }
  const sel = view.state.selection as { node?: ProseNode }
  if (sel.node?.type.name === 'code_block') return { kind: 'code', node: sel.node, pos: view.state.selection.from }
  if (sel.node?.type.name === 'table') return { kind: 'table', node: sel.node, pos: view.state.selection.from }
  return null
}

let lineNumbersVisible = true

export function setLineNumbersVisible(visible: boolean): void {
  lineNumbersVisible = visible
  document.documentElement.classList.toggle('mp-hide-gutters', !visible)
}

function buttonsFor(ctx: Ctx, target: Target, view: EditorView, rerender: () => void): BarButton[] {
  const t = targetFromCtx(ctx)
  if (target.kind === 'table') {
    return [
      { label: '↑행', title: '위에 행 추가', run: () => cmd.tableOp(t, 'addRowAbove') },
      { label: '↓행', title: '아래에 행 추가', run: () => cmd.tableOp(t, 'addRowBelow') },
      { label: '←열', title: '왼쪽에 열 추가', run: () => cmd.tableOp(t, 'addColLeft') },
      { label: '→열', title: '오른쪽에 열 추가', run: () => cmd.tableOp(t, 'addColRight') },
      { label: '행 삭제', title: '현재 행 삭제', run: () => cmd.tableOp(t, 'delRow') },
      { label: '열 삭제', title: '현재 열 삭제', run: () => cmd.tableOp(t, 'delCol') },
      { label: '⇤', title: '왼쪽 정렬', run: () => cmd.tableOp(t, 'alignLeft') },
      { label: '⇔', title: '가운데 정렬', run: () => cmd.tableOp(t, 'alignCenter') },
      { label: '⇥', title: '오른쪽 정렬', run: () => cmd.tableOp(t, 'alignRight') },
      { label: '표 삭제', title: '표 전체 삭제', run: () => cmd.tableOp(t, 'delete') },
    ]
  }
  const lang = String(target.node.attrs['language'] ?? '') || 'text'
  return [
    { label: lang, title: '언어 (블록 상단의 언어 선택기로 변경)', run: () => {} },
    {
      label: '복사',
      title: '코드 복사',
      run: () => {
        void navigator.clipboard.writeText(target.node.textContent)
      },
    },
    {
      label: lineNumbersVisible ? '줄 번호 숨김' : '줄 번호 표시',
      title: '줄 번호 토글',
      run: () => {
        setLineNumbersVisible(!lineNumbersVisible)
        rerender()
      },
    },
    {
      label: '블록 삭제',
      title: '코드블록 삭제',
      run: () => {
        view.dispatch(view.state.tr.delete(target.pos, target.pos + target.node.nodeSize).scrollIntoView())
      },
    },
  ]
}

export const contextBarPlugin = $prose((ctx) => {
  let bar: HTMLDivElement | null = null
  let current: Target | null = null

  const ensureBar = (view: EditorView): HTMLDivElement => {
    if (bar) return bar
    bar = document.createElement('div')
    bar.className = 'mp-context-bar'
    bar.addEventListener('mousedown', (e) => e.preventDefault()) // keep editor focus
    const root = view.dom.closest('.markpad-editor') ?? view.dom.parentElement ?? document.body
    root.appendChild(bar)
    return bar
  }

  const render = (view: EditorView): void => {
    const target = findTarget(view)
    if (!target) {
      current = null
      if (bar) bar.style.display = 'none'
      return
    }
    const el = ensureBar(view)
    const dom = view.nodeDOM(target.pos) as HTMLElement | null
    if (!dom) {
      el.style.display = 'none'
      return
    }
    const sameTarget = current && current.pos === target.pos && current.kind === target.kind && current.node === target.node
    if (!sameTarget) {
      el.replaceChildren()
      for (const b of buttonsFor(ctx, target, view, () => {
        current = null
        render(view)
      })) {
        const btn = document.createElement('button')
        btn.type = 'button'
        btn.textContent = b.label
        btn.title = b.title
        btn.addEventListener('click', () => {
          b.run()
          view.focus()
        })
        el.appendChild(btn)
      }
      current = target
    }
    const rootRect = el.offsetParent?.getBoundingClientRect() ?? { top: 0, left: 0 }
    const rect = dom.getBoundingClientRect()
    el.style.display = 'flex'
    el.style.left = `${Math.max(8, rect.left - rootRect.left)}px`
    el.style.top = `${Math.max(0, rect.top - rootRect.top - el.offsetHeight - 6)}px`
  }

  return new Plugin({
    key: new PluginKey('markpad-context-bar'),
    view: (view) => {
      render(view)
      const onScroll = (): void => render(view)
      window.addEventListener('scroll', onScroll, true)
      window.addEventListener('resize', onScroll)
      return {
        update: (v) => render(v),
        destroy: () => {
          window.removeEventListener('scroll', onScroll, true)
          window.removeEventListener('resize', onScroll)
          bar?.remove()
          bar = null
        },
      }
    },
  })
})

export function hideContextBar(ctx: Ctx): void {
  const view = ctx.get(editorViewCtx)
  view.dom.closest('.markpad-editor')?.querySelector('.mp-context-bar')?.setAttribute('style', 'display:none')
}
