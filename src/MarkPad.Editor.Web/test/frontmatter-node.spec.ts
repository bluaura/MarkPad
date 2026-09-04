/** T-48: the front matter node round-trips through the real engine and stays byte-exact when untouched. */
import { editorViewCtx } from '@milkdown/kit/core'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { roundTrip } from '../src/roundtrip'
import { createHeadlessEditor, type HeadlessEditor } from './helpers/headless-editor'

let editor: HeadlessEditor

beforeAll(async () => {
  editor = await createHeadlessEditor()
})

afterAll(async () => {
  await editor.destroy()
})

describe('front matter node (T-48)', () => {
  const doc = '---\ntitle: "Hi"  # c\ntags: [a, b]\n---\n\n# Body\n\ntext\n'

  it('parses into a frontmatter node and serializes back', async () => {
    const md = await editor.roundTripMarkdown(doc)
    expect(md.startsWith('---\ntitle: "Hi"  # c\ntags: [a, b]\n---')).toBe(true)
    expect(md).toContain('# Body')
  })

  it('is preserved byte-exact by roundTrip when untouched', async () => {
    const current = await editor.roundTripMarkdown(doc)
    expect(roundTrip(doc, current).text).toBe(doc)
  })

  it('edited raw attr is written back as a yaml block', async () => {
    const { replaceAll } = await import('@milkdown/kit/utils')
    editor.crepe.editor.action(replaceAll(doc, true))
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    const first = view.state.doc.firstChild!
    expect(first.type.name).toBe('frontmatter')
    view.dispatch(view.state.tr.setNodeMarkup(0, undefined, { raw: 'title: Changed' }))
    const out = editor.crepe.getMarkdown()
    expect(out.startsWith('---\ntitle: Changed\n---')).toBe(true)
    const rt = roundTrip(doc, out)
    expect(rt.text).toBe('---\ntitle: Changed\n---\n\n# Body\n\ntext\n')
    expect(rt.changedBlocks).toEqual([0])
  })
})
