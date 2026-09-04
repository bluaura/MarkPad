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
import { callCommand } from '@milkdown/kit/utils'
import { BridgeError } from './bridge'
import type { FormatListParams, FormatToggleParams } from './bridge-types'
import type { MarkPadEditor } from './editor'
import { computeSelectionContext } from './selection'

/** Toolbar → Milkdown command mapping (ARCHITECTURE §3.3, PRD Appendix A). */

export function toggleMark(editor: MarkPadEditor, mark: FormatToggleParams['mark']): void {
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

export function setHeading(editor: MarkPadEditor, level: number): void {
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

export function setList(editor: MarkPadEditor, type: FormatListParams['type']): void {
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
      view.dispatch(setTaskChecked(view.state, false))
      return
  }
}

export function indent(editor: MarkPadEditor): void {
  editor.action(callCommand(sinkListItemCommand.key))
}

export function outdent(editor: MarkPadEditor): void {
  editor.action(callCommand(liftListItemCommand.key))
}

export function toggleBlockquote(editor: MarkPadEditor): void {
  const view = editor.view()
  if (computeSelectionContext(view.state).blockquote) {
    lift(view.state, view.dispatch)
    return
  }
  editor.action(callCommand(wrapInBlockquoteCommand.key))
}

export function clearFormat(editor: MarkPadEditor): void {
  const view = editor.view()
  const { from, to, empty } = view.state.selection
  if (empty) return
  let tr = view.state.tr
  for (const mark of Object.values(view.state.schema.marks)) {
    tr = tr.removeMark(from, to, mark)
  }
  view.dispatch(tr)
}

export function insertCodeBlock(editor: MarkPadEditor, lang = ''): void {
  editor.action(callCommand(createCodeBlockCommand.key, lang))
}

export function insertHr(editor: MarkPadEditor): void {
  editor.action(callCommand(insertHrCommand.key))
}

export function insertTable(editor: MarkPadEditor, rows: number, cols: number): void {
  editor.action(callCommand(insertTableCommand.key, { row: Math.max(1, rows), col: Math.max(1, cols) }))
}

export function insertLink(editor: MarkPadEditor, href: string, text?: string): void {
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

export function insertImage(editor: MarkPadEditor, src: string, alt = ''): void {
  editor.action(callCommand(insertImageCommand.key, { src, alt }))
}

export function insertText(editor: MarkPadEditor, text: string): void {
  const view = editor.view()
  view.dispatch(view.state.tr.insertText(text).scrollIntoView())
}

export function undo(editor: MarkPadEditor): void {
  editor.action(callCommand(undoCommand.key))
}

export function redo(editor: MarkPadEditor): void {
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

export function tableOp(editor: MarkPadEditor, op: TableOp): void {
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

export function selectAll(editor: MarkPadEditor): void {
  const view = editor.view()
  view.dispatch(view.state.tr.setSelection(new AllSelection(view.state.doc)))
}
