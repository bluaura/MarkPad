/**
 * T-34 (two-level alignment for lists/blockquotes) and T-36 (serialization style only affects changed blocks),
 * through the real Crepe engine.
 */
import { editorViewCtx } from '@milkdown/kit/core'
import { TextSelection } from '@milkdown/kit/prose/state'
import { replaceAll } from '@milkdown/kit/utils'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { defaultEditorSettings } from '../src/crepe-factory'
import { roundTrip } from '../src/roundtrip'
import { createHeadlessEditor, type HeadlessEditor } from './helpers/headless-editor'

describe('roundTrip() list/blockquote recursion (T-34)', () => {
  it('keeps untouched list items byte-identical when one item changes', () => {
    const original = '* first  item\n*   second item\n* third **item**\n'
    const current = '- first  item\n- second item CHANGED\n- third **item**\n'
    const r = roundTrip(original, current)
    expect(r.text).toBe('* first  item\n* second item CHANGED\n* third **item**\n')
    expect(r.changedBlocks).toEqual([0])
  })

  it('adopts the original marker for inserted items and keeps ordered delimiters', () => {
    const original = '1) one\n2) two\n'
    const current = '1. one\n2. two\n3. three\n'
    expect(roundTrip(original, current).text).toBe('1) one\n2) two\n3) three\n')
  })

  it('drops removed items and keeps the rest', () => {
    const original = '- a\n- b\n- c\n'
    const current = '- a\n- c\n'
    expect(roundTrip(original, current).text).toBe('- a\n- c\n')
  })

  it('merges blockquote children', () => {
    const original = '> line __one__\n>\n> line two\n'
    const current = '> line **one**\n>\n> line two edited\n'
    expect(roundTrip(original, current).text).toBe('> line __one__\n>\n> line two edited\n')
  })

  it('falls back to whole-block replacement when nothing inside matches', () => {
    const original = '- a\n- b\n'
    const current = '- x\n- y\n'
    expect(roundTrip(original, current).text).toBe('- x\n- y\n')
  })
})

describe('serialization style settings (T-36)', () => {
  let editor: HeadlessEditor

  beforeAll(async () => {
    editor = await createHeadlessEditor({
      ...defaultEditorSettings,
      markdown: { ...defaultEditorSettings.markdown, bullet: '*' },
    })
  })

  afterAll(async () => {
    await editor.destroy()
  })

  it('new blocks use the configured style, untouched blocks keep the original', () => {
    const original = '- keep *this*\n- and this\n\nParagraph.\n'
    editor.crepe.editor.action(replaceAll(original, true))
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    // Append " more" to the paragraph (last block) and add a new emphasised word.
    const end = view.state.doc.content.size - 1
    view.dispatch(view.state.tr.setSelection(TextSelection.create(view.state.doc, end)).insertText(' more'))
    const current = editor.crepe.getMarkdown()
    expect(current).toContain('* keep *this*') // engine restyled the bullets…
    const r = roundTrip(original, current)
    expect(r.text).toBe('- keep *this*\n- and this\n\nParagraph. more\n') // …but only the edited block is written
  })
})
