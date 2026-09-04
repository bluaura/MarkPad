import type { EditorState } from '@milkdown/kit/prose/state'
import { NodeSelection } from '@milkdown/kit/prose/state'
import type { SelectionContext } from './bridge-types'

/** Derive the toolbar toggle state from the current ProseMirror selection (ARCHITECTURE §3.2). */
export function computeSelectionContext(state: EditorState): SelectionContext {
  const { from, to, empty, $from } = state.selection
  const schema = state.schema

  const hasMark = (name: string): boolean => {
    const type = schema.marks[name]
    if (!type) return false
    if (empty) return !!type.isInSet(state.storedMarks ?? $from.marks())
    return state.doc.rangeHasMark(from, to, type)
  }

  let link: string | null = null
  const linkType = schema.marks['link']
  if (linkType) {
    const mark = linkType.isInSet($from.marks())
    if (mark) link = String(mark.attrs['href'] ?? '')
  }

  const ctx: SelectionContext = {
    bold: hasMark('strong'),
    italic: hasMark('emphasis'),
    strike: hasMark('strike_through'),
    code: hasMark('inlineCode'),
    highlight: hasMark('highlight'),
    heading: 0,
    list: null,
    blockquote: false,
    inTable: false,
    inCode: false,
    codeLang: null,
    link,
    hasSelection: !empty,
  }

  if (state.selection instanceof NodeSelection && state.selection.node.type.name === 'code_block') {
    ctx.inCode = true
    ctx.codeLang = String(state.selection.node.attrs['language'] ?? '')
  }

  for (let depth = $from.depth; depth >= 0; depth--) {
    const node = $from.node(depth)
    switch (node.type.name) {
      case 'heading':
        ctx.heading = Number(node.attrs['level'] ?? 0)
        break
      case 'list_item':
        if (ctx.list === null) {
          if (node.attrs['checked'] !== null && node.attrs['checked'] !== undefined) ctx.list = 'task'
          else ctx.list = depth > 0 && $from.node(depth - 1).type.name === 'ordered_list' ? 'ordered' : 'bullet'
        }
        break
      case 'blockquote':
        ctx.blockquote = true
        break
      case 'table':
        ctx.inTable = true
        break
      case 'code_block':
        ctx.inCode = true
        ctx.codeLang = String(node.attrs['language'] ?? '')
        break
    }
  }
  return ctx
}

export interface DocStats {
  words: number
  chars: number
}

/** Word/character statistics over the document's text content (block separators count as spaces). */
export function computeStats(state: EditorState): DocStats {
  const text = state.doc.textBetween(0, state.doc.content.size, ' ', ' ')
  const words = text.split(/\s+/).filter((w) => w.length > 0).length
  const chars = text.replace(/\s+/g, '').length
  return { words, chars }
}

/** 1-based index of the top-level block that contains the selection head. */
export function currentLine(state: EditorState): number {
  return state.selection.$head.index(0) + 1
}
