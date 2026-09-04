import { bridge, BridgeError } from '../bridge'
import type { DocLoadParams, DocSerializeParams, DocSerializeResult, SetReadonlyParams } from '../bridge-types'
import { createEditor } from '../editor'
import { joinFrontMatter, splitFrontMatter } from '../frontmatter'
import { roundTrip } from '../roundtrip'
import { getSession, requireSession, setSession } from '../session'
import { applyTheme } from '../theme'

const LOAD_TIMEOUT_NOTE = 'doc.load' // host uses a 60s timeout for this method (ARCHITECTURE §4.2)

function editorRoot(): HTMLElement {
  const el = document.getElementById('editor')
  if (!el) throw new BridgeError('INTERNAL', '#editor root missing')
  return el
}

export function registerDocHandlers(): void {
  bridge.register(LOAD_TIMEOUT_NOTE, async (raw) => {
    const p = raw as DocLoadParams
    if (typeof p?.text !== 'string') throw new BridgeError('BAD_PARAM', 'text required')
    if (!p.settings) throw new BridgeError('BAD_PARAM', 'settings required')

    const previous = getSession()
    if (previous) {
      setSession(null)
      await previous.editor.destroy()
    }

    applyTheme(p.settings.theme)
    const text = p.text.replace(/\r\n?/g, '\n')
    const fm = p.settings.markdown.frontMatter ? splitFrontMatter(text) : { raw: '', body: text }

    const editor = await createEditor(editorRoot(), fm.body, p.settings)
    if (p.readonly) editor.setReadonly(true)
    setSession({ editor, settings: p.settings, path: p.path ?? null, frontMatterRaw: fm.raw })
    return { ok: true }
  })

  bridge.register('doc.getMarkdown', () => {
    const s = requireSession()
    return { text: joinFrontMatter(s.frontMatterRaw, s.editor.getMarkdown()) }
  })

  bridge.register('doc.serializeForSave', (raw): DocSerializeResult => {
    const s = requireSession()
    const p = raw as DocSerializeParams
    const original = (p?.original ?? '').replace(/\r\n?/g, '\n')
    const originalSplit = s.settings.markdown.frontMatter ? splitFrontMatter(original) : { raw: '', body: original }
    const current = s.editor.getMarkdown()
    const result = roundTrip(originalSplit.body, current)
    // Front matter is opaque to the editor in this milestone: keep the loaded raw block verbatim.
    return { text: joinFrontMatter(s.frontMatterRaw, result.text), changedBlocks: result.changedBlocks }
  })

  bridge.register('doc.setReadonly', (raw) => {
    const p = raw as SetReadonlyParams
    requireSession().editor.setReadonly(!!p?.value)
    return null
  })

  bridge.register('doc.isDirty', () => ({ dirty: requireSession().editor.isDirty() }))

  bridge.register('doc.markSaved', () => {
    requireSession().editor.markSaved()
    return null
  })
}
