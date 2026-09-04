import { bridge, BridgeError } from '../bridge'
import type {
  InsertCodeBlockParams,
  InsertImageParams,
  InsertLinkParams,
  InsertTableParams,
  InsertTextParams,
} from '../bridge-types'
import * as cmd from '../commands'
import { requireSession } from '../session'

export function registerInsertHandlers(): void {
  bridge.register('insert.codeBlock', (raw) => {
    const p = raw as InsertCodeBlockParams | undefined
    cmd.insertCodeBlock(requireSession().editor, p?.lang ?? '')
    return null
  })

  bridge.register('insert.hr', () => {
    cmd.insertHr(requireSession().editor)
    return null
  })

  bridge.register('insert.table', (raw) => {
    const p = raw as InsertTableParams | undefined
    cmd.insertTable(requireSession().editor, p?.rows ?? 3, p?.cols ?? 3)
    return null
  })

  bridge.register('insert.link', (raw) => {
    const p = raw as InsertLinkParams
    if (!p?.href) throw new BridgeError('BAD_PARAM', 'href required')
    cmd.insertLink(requireSession().editor, p.href, p.text)
    return null
  })

  bridge.register('insert.image', (raw) => {
    const p = raw as InsertImageParams
    if (!p?.src) throw new BridgeError('BAD_PARAM', 'src required')
    cmd.insertImage(requireSession().editor, p.src, p.alt ?? '')
    return null
  })

  bridge.register('insert.datetime', (raw) => {
    const p = raw as InsertTextParams
    cmd.insertText(requireSession().editor, p?.text ?? formatNow())
    return null
  })

  const tableOps: cmd.TableOp[] = [
    'addRowAbove',
    'addRowBelow',
    'addColLeft',
    'addColRight',
    'delRow',
    'delCol',
    'delete',
    'alignLeft',
    'alignCenter',
    'alignRight',
  ]
  for (const op of tableOps) {
    bridge.register(`table.${op}`, () => {
      cmd.tableOp(requireSession().editor, op)
      return null
    })
  }
}

function formatNow(): string {
  const d = new Date()
  const pad = (n: number): string => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
