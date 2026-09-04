/** T-58b: alt/path edits go through node attrs and serialize as `![alt](path)`; deleting keeps the file (nothing touches disk). */
import { editorViewCtx } from '@milkdown/kit/core'
import { replaceAll } from '@milkdown/kit/utils'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { createHeadlessEditor, type HeadlessEditor } from './helpers/headless-editor'

let editor: HeadlessEditor

beforeAll(async () => {
  editor = await createHeadlessEditor()
})

afterAll(async () => {
  await editor.destroy()
})

describe('image properties (F-IMG-05)', () => {
  it('block image alt/src edit serializes exactly', () => {
    editor.crepe.editor.action(replaceAll('![old alt](assets/a.png)\n', true))
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    let pos = -1
    view.state.doc.descendants((n, p) => {
      if (n.type.name === 'image-block') pos = p
      return pos < 0
    })
    expect(pos).toBeGreaterThanOrEqual(0)
    const node = view.state.doc.nodeAt(pos)!
    view.dispatch(view.state.tr.setNodeMarkup(pos, undefined, { ...node.attrs, alt: 'new alt', src: 'assets/b.png' }))
    expect(editor.crepe.getMarkdown().trim()).toBe('![new alt](assets/b.png)')
  })

  it('inline image inside a paragraph keeps surrounding text', () => {
    editor.crepe.editor.action(replaceAll('before ![i](x.png) after\n', true))
    const view = editor.crepe.editor.ctx.get(editorViewCtx)
    let pos = -1
    view.state.doc.descendants((n, p) => {
      if (n.type.name === 'image') pos = p
      return pos < 0
    })
    const node = view.state.doc.nodeAt(pos)!
    view.dispatch(view.state.tr.setNodeMarkup(pos, undefined, { ...node.attrs, alt: 'img', src: 'y.png' }))
    expect(editor.crepe.getMarkdown().trim()).toBe('before ![img](y.png) after')
    view.dispatch(view.state.tr.delete(pos, pos + node.nodeSize))
    expect(editor.crepe.getMarkdown().trim()).toBe('before  after')
  })
})
