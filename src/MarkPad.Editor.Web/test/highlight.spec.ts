/** `==highlight==` extension: parse → mark → serialize, toggle command, input rule, and OFF = plain text. */
import { editorViewCtx } from '@milkdown/kit/core'
import { TextSelection } from '@milkdown/kit/prose/state'
import { replaceAll } from '@milkdown/kit/utils'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import * as cmd from '../src/commands'
import { defaultEditorSettings } from '../src/crepe-factory'
import { roundTrip } from '../src/roundtrip'
import { createHeadlessEditor, type HeadlessEditor } from './helpers/headless-editor'

describe('highlight extension ON', () => {
  let editor: HeadlessEditor

  beforeAll(async () => {
    editor = await createHeadlessEditor({ ...defaultEditorSettings, markdown: { ...defaultEditorSettings.markdown, extHighlight: true } })
  })

  afterAll(async () => {
    await editor.destroy()
  })

  it('round-trips ==text== through the engine byte-exact', async () => {
    const md = 'A ==marked== word and ==**bold** mark==.\n'
    expect(await editor.roundTripMarkdown(md)).toBe(md)
    expect(roundTrip(md, await editor.roundTripMarkdown(md)).text).toBe(md)
  })

  it('creates a highlight mark in the document', () => {
    editor.crepe.editor.action(replaceAll('x ==hi== y', true))
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    let found = false
    view.state.doc.descendants((n) => {
      if (n.isText && n.marks.some((m) => m.type.name === 'highlight') && n.text === 'hi') found = true
      return true
    })
    expect(found).toBe(true)
  })

  it('toolbar toggle wraps the selection and toggles back', () => {
    editor.crepe.editor.action(replaceAll('hello world', true))
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    view.dispatch(view.state.tr.setSelection(TextSelection.create(view.state.doc, 1, 6)))
    const target: cmd.CommandTarget = { action: (fn) => editor.crepe.editor.action(fn), view: () => view }
    cmd.toggleMark(target, 'highlight')
    expect(editor.crepe.getMarkdown().trim()).toBe('==hello== world')
    cmd.toggleMark(target, 'highlight')
    expect(editor.crepe.getMarkdown().trim()).toBe('hello world')
  })

  it('input rule: typing ==text== applies the mark', () => {
    editor.crepe.editor.action(replaceAll('', true))
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    view.dispatch(view.state.tr.setSelection(TextSelection.create(view.state.doc, 1)))
    for (const ch of '==note==') {
      const { from, to } = view.state.selection
      const handled = view.someProp('handleTextInput', (f) => f(view, from, to, ch, () => view.state.tr.insertText(ch, from, to)))
      if (!handled) view.dispatch(view.state.tr.insertText(ch, from, to))
    }
    expect(editor.crepe.getMarkdown().trim()).toBe('==note==')
  })

  it('does not touch code spans', async () => {
    const md = '`==not==` and ==yes==\n'
    expect(await editor.roundTripMarkdown(md)).toBe(md)
  })
})

describe('highlight extension OFF (default)', () => {
  it('leaves == as plain text and no highlight mark exists', async () => {
    const editor = await createHeadlessEditor()
    const md = 'A ==marked== word.\n'
    expect(await editor.roundTripMarkdown(md)).toBe(md)
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    expect(view.state.schema.marks['highlight']).toBeUndefined()
    await editor.destroy()
  })
})
