import { bridge } from '../bridge'
import type { FindParams, FindReplaceParams, FindResult } from '../bridge-types'
import { findClear, findGetState, findReplace, findReplaceAll, findSet, findStep } from '../plugins/find'
import { requireSession } from '../session'

function result(): FindResult {
  const state = findGetState(requireSession().editor.view().state)
  return { count: state?.matches.length ?? 0, index: state?.index ?? -1 }
}

export function registerFindHandlers(): void {
  bridge.register('find.set', (raw): FindResult => {
    const p = raw as FindParams
    const view = requireSession().editor.view()
    const s = findSet(view, { query: p?.query ?? '', caseSensitive: !!p?.caseSensitive, wholeWord: !!p?.wholeWord })
    return { count: s.matches.length, index: s.index }
  })

  bridge.register('find.next', (): FindResult => {
    const s = findStep(requireSession().editor.view(), 1)
    return { count: s.matches.length, index: s.index }
  })

  bridge.register('find.prev', (): FindResult => {
    const s = findStep(requireSession().editor.view(), -1)
    return { count: s.matches.length, index: s.index }
  })

  bridge.register('find.replace', (raw): FindResult => {
    const p = raw as FindReplaceParams
    const s = findReplace(requireSession().editor.view(), p?.replacement ?? '')
    return { count: s.matches.length, index: s.index }
  })

  bridge.register('find.replaceAll', (raw): FindResult & { replaced: number } => {
    const p = raw as FindReplaceParams
    const replaced = findReplaceAll(requireSession().editor.view(), p?.replacement ?? '')
    return { ...result(), replaced }
  })

  bridge.register('find.clear', (): FindResult => {
    findClear(requireSession().editor.view())
    return result()
  })
}
