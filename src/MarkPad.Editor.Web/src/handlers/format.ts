import { bridge, BridgeError } from '../bridge'
import type { FormatHeadingParams, FormatListParams, FormatToggleParams } from '../bridge-types'
import * as cmd from '../commands'
import { requireSession } from '../session'

export function registerFormatHandlers(): void {
  bridge.register('format.toggle', (raw) => {
    const p = raw as FormatToggleParams
    if (!p?.mark) throw new BridgeError('BAD_PARAM', 'mark required')
    cmd.toggleMark(requireSession().editor, p.mark)
    return null
  })

  bridge.register('format.heading', (raw) => {
    const p = raw as FormatHeadingParams
    cmd.setHeading(requireSession().editor, Number(p?.level ?? 0))
    return null
  })

  bridge.register('format.list', (raw) => {
    const p = raw as FormatListParams
    if (!p?.type) throw new BridgeError('BAD_PARAM', 'type required')
    cmd.setList(requireSession().editor, p.type)
    return null
  })

  bridge.register('format.indent', () => {
    cmd.indent(requireSession().editor)
    return null
  })

  bridge.register('format.outdent', () => {
    cmd.outdent(requireSession().editor)
    return null
  })

  bridge.register('format.blockquote', () => {
    cmd.toggleBlockquote(requireSession().editor)
    return null
  })

  bridge.register('format.clear', () => {
    cmd.clearFormat(requireSession().editor)
    return null
  })
}
