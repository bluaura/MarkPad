import type { Ctx } from '@milkdown/kit/ctx'
import { redoCommand, undoCommand } from '@milkdown/kit/plugin/history'
import {
  createCodeBlockCommand,
  insertHrCommand,
  insertImageCommand,
  liftListItemCommand,
  sinkListItemCommand,
  toggleEmphasisCommand,
  toggleInlineCodeCommand,
  toggleLinkCommand,
  toggleStrongCommand,
  turnIntoTextCommand,
  wrapInBlockquoteCommand,
  wrapInBulletListCommand,
  wrapInHeadingCommand,
  wrapInOrderedListCommand,
} from '@milkdown/kit/preset/commonmark'
import {
  addColAfterCommand,
  addColBeforeCommand,
  addRowAfterCommand,
  addRowBeforeCommand,
  deleteSelectedCellsCommand,
  insertTableCommand,
  selectColCommand,
  selectRowCommand,
  selectTableCommand,
  setAlignCommand,
  toggleStrikethroughCommand,
} from '@milkdown/kit/preset/gfm'
import { lift } from '@milkdown/kit/prose/commands'
import { AllSelection, type EditorState, type Transaction } from '@milkdown/kit/prose/state'
import type { EditorView } from '@milkdown/kit/prose/view'
import { callCommand, insert } from '@milkdown/kit/utils'
import { BridgeError } from './bridge'
import type { FormatListParams, FormatToggleParams } from './bridge-types'
import { computeSelectionContext } from './selection'

/** Minimal editor handle the commands need; satisfied by MarkPadEditor and by a Ctx adapter (keymaps). */
export interface CommandTarget {
  action<T>(fn: (ctx: Ctx) => T): T
  view(): EditorView
}

/** Toolbar → Milkdown command mapping (ARCHITECTURE §3.3, PRD Appendix A). */

export function toggleMark(editor: CommandTarget, mark: FormatToggleParams['mark']): void {
  switch (mark) {
    case 'bold':
      editor.action(callCommand(toggleStrongCommand.key))
      return
    case 'italic':
      editor.action(callCommand(toggleEmphasisCommand.key))
      return
    case 'strike':
      editor.action(callCommand(toggleStrikethroughCommand.key))
      return
    case 'code':
      editor.action(callCommand(toggleInlineCodeCommand.key))
      return
    case 'highlight':
      throw new BridgeError('UNSUPPORTED', 'highlight extension is not enabled')
  }
}

export function setHeading(editor: CommandTarget, level: number): void {
  if (level <= 0) {
    editor.action(callCommand(turnIntoTextCommand.key))
    return
  }
  if (level > 6) throw new BridgeError('BAD_PARAM', `heading level ${level}`)
  editor.action(callCommand(wrapInHeadingCommand.key, level))
}

/** Set `checked` on every list item that intersects the selection (null = plain bullet). */
function setTaskChecked(state: EditorState, checked: boolean | null): Transaction {
  const { from, to } = state.selection
  const tr = state.tr
  state.doc.nodesBetween(from, to, (node, pos) => {
    if (node.type.name === 'list_item') {
      tr.setNodeMarkup(pos, undefined, { ...node.attrs, checked })
    }
    return true
  })
  return tr
}

export function setList(editor: CommandTarget, type: FormatListParams['type']): void {
  const view = editor.view()
  const ctx = computeSelectionContext(view.state)

  if (ctx.list === type) {
    // Toggle off: un-task first, then lift out of the list.
    if (type === 'task') view.dispatch(setTaskChecked(view.state, null))
    editor.action(callCommand(liftListItemCommand.key))
    return
  }

  switch (type) {
    case 'bullet':
      if (ctx.list === 'task') {
        view.dispatch(setTaskChecked(view.state, null))
      } else {
        if (ctx.list === 'ordered') editor.action(callCommand(liftListItemCommand.key))
        editor.action(callCommand(wrapInBulletListCommand.key))
      }
      return
    case 'ordered':
      if (ctx.list) editor.action(callCommand(liftListItemCommand.key))
      editor.action(callCommand(wrapInOrderedListCommand.key))
      return
    case 'task':
      if (ctx.list === 'ordered') editor.action(callCommand(liftListItemCommand.key))
      if (ctx.list !== 'bullet') editor.action(callCommand(wrapInBulletListCommand.key))
      view.dispatch(setTaskChecked(editor.view().state, false))
      return
  }
}

export function indent(editor: CommandTarget): void {
  editor.action(callCommand(sinkListItemCommand.key))
}

export function outdent(editor: CommandTarget): void {
  editor.action(callCommand(liftListItemCommand.key))
}

export function toggleBlockquote(editor: CommandTarget): void {
  const view = editor.view()
  if (computeSelectionContext(view.state).blockquote) {
    lift(view.state, view.dispatch)
    return
  }
  editor.action(callCommand(wrapInBlockquoteCommand.key))
}

export function clearFormat(editor: CommandTarget): void {
  const view = editor.view()
  const { from, to, empty } = view.state.selection
  if (empty) return
  let tr = view.state.tr
  for (const mark of Object.values(view.state.schema.marks)) {
    tr = tr.removeMark(from, to, mark)
  }
  view.dispatch(tr)
}

export function insertCodeBlock(editor: CommandTarget, lang = ''): void {
  editor.action(callCommand(createCodeBlockCommand.key, lang))
}

export function insertHr(editor: CommandTarget): void {
  editor.action(callCommand(insertHrCommand.key))
}

export function insertTable(editor: CommandTarget, rows: number, cols: number): void {
  editor.action(callCommand(insertTableCommand.key, { row: Math.max(1, rows), col: Math.max(1, cols) }))
}

export function insertLink(editor: CommandTarget, href: string, text?: string): void {
  const view = editor.view()
  const { empty } = view.state.selection
  if (empty) {
    const label = text && text.length > 0 ? text : href
    const linkType = view.state.schema.marks['link']
    if (!linkType) throw new BridgeError('INTERNAL', 'link mark missing')
    const node = view.state.schema.text(label, [linkType.create({ href })])
    view.dispatch(view.state.tr.replaceSelectionWith(node, false).scrollIntoView())
    return
  }
  editor.action(callCommand(toggleLinkCommand.key, { href }))
}

export function insertImage(editor: CommandTarget, src: string, alt = ''): void {
  editor.action(callCommand(insertImageCommand.key, { src, alt }))
}

export function insertText(editor: CommandTarget, text: string): void {
  const view = editor.view()
  view.dispatch(view.state.tr.insertText(text).scrollIntoView())
}

/** PRD F-VIEW-06: inline `$…$` or display `$$…$$` math. Inserted as markdown so the Latex feature builds the node. */
export function insertMath(editor: CommandTarget, display: boolean): void {
  const view = editor.view()
  const selected = view.state.doc.textBetween(view.state.selection.from, view.state.selection.to, ' ')
  const body = selected || 'E = mc^2'
  if (display) {
    editor.action(insert(`$$\n${body}\n$$`))
  } else {
    editor.action(insert(`$${body}$`, true))
  }
}

/** Footnote: `[^n]` reference at the cursor plus a definition block appended to the document. */
export function insertFootnote(editor: CommandTarget, text = ''): void {
  const view = editor.view()
  const { schema, doc } = view.state
  const refType = schema.nodes['footnote_reference']
  const defType = schema.nodes['footnote_definition']
  if (!refType || !defType) throw new BridgeError('UNSUPPORTED', 'footnotes not available')
  let n = 0
  doc.descendants((node) => {
    if (node.type === defType) n++
    return false
  })
  const label = String(n + 1)
  const paragraph = schema.nodes['paragraph']!.create(null, text ? schema.text(text) : undefined)
  const definition = defType.create({ label }, paragraph)
  let tr = view.state.tr.replaceSelectionWith(refType.create({ label }), false)
  tr = tr.insert(tr.doc.content.size, definition)
  view.dispatch(tr.scrollIntoView())
}

export function undo(editor: CommandTarget): void {
  editor.action(callCommand(undoCommand.key))
}

export function redo(editor: CommandTarget): void {
  editor.action(callCommand(redoCommand.key))
}

export type TableOp =
  | 'addRowAbove'
  | 'addRowBelow'
  | 'addColLeft'
  | 'addColRight'
  | 'delRow'
  | 'delCol'
  | 'delete'
  | 'alignLeft'
  | 'alignCenter'
  | 'alignRight'

export function tableOp(editor: CommandTarget, op: TableOp): void {
  const run = (key: Parameters<typeof callCommand>[0], payload?: unknown): void => {
    editor.action(callCommand(key, payload))
  }
  switch (op) {
    case 'addRowAbove':
      run(addRowBeforeCommand.key)
      return
    case 'addRowBelow':
      run(addRowAfterCommand.key)
      return
    case 'addColLeft':
      run(addColBeforeCommand.key)
      return
    case 'addColRight':
      run(addColAfterCommand.key)
      return
    case 'delRow':
      run(selectRowCommand.key)
      run(deleteSelectedCellsCommand.key)
      return
    case 'delCol':
      run(selectColCommand.key)
      run(deleteSelectedCellsCommand.key)
      return
    case 'delete':
      run(selectTableCommand.key)
      run(deleteSelectedCellsCommand.key)
      return
    case 'alignLeft':
      run(setAlignCommand.key, 'left')
      return
    case 'alignCenter':
      run(setAlignCommand.key, 'center')
      return
    case 'alignRight':
      run(setAlignCommand.key, 'right')
      return
  }
}

export function selectAll(editor: CommandTarget): void {
  const view = editor.view()
  view.dispatch(view.state.tr.setSelection(new AllSelection(view.state.doc)))
}
