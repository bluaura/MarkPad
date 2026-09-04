/**
 * T-26/T-29/T-31 engine behaviour through the real Crepe schema: input rules, checklist toggling and
 * find/replace, all headless (jsdom).
 */
import { editorViewCtx } from '@milkdown/kit/core'
import { TextSelection } from '@milkdown/kit/prose/state'
import { replaceAll } from '@milkdown/kit/utils'
import { beforeAll, afterAll, describe, expect, it } from 'vitest'
import * as cmd from '../src/commands'
import { findMatches, findReplaceAll, findSet, findStep } from '../src/plugins/find'
import { createHeadlessEditor, type HeadlessEditor } from './helpers/headless-editor'

let editor: HeadlessEditor

function load(markdown: string): void {
  editor.crepe.editor.action(replaceAll(markdown, true))
}

function view() {
  return editor.crepe.editor.ctx.get(editorViewCtx)
}

/** Simulate typing at the current selection the way ProseMirror does (input rules hook handleTextInput). */
function type(text: string): void {
  const v = view()
  for (const ch of text) {
    const { from, to } = v.state.selection
    const handled = v.someProp('handleTextInput', (f) => f(v, from, to, ch, () => v.state.tr.insertText(ch, from, to)))
    if (!handled) v.dispatch(v.state.tr.insertText(ch, from, to))
  }
}

function placeCursorAtEndOfFirstBlock(): void {
  const v = view()
  const first = v.state.doc.firstChild!
  v.dispatch(v.state.tr.setSelection(TextSelection.create(v.state.doc, first.nodeSize - 1)))
}

const target: cmd.CommandTarget = {
  action: (fn) => editor.crepe.editor.action(fn),
  view: () => view(),
}

beforeAll(async () => {
  editor = await createHeadlessEditor()
})

afterAll(async () => {
  await editor.destroy()
})

describe('input rules (F-EDIT-04)', () => {
  it.each([
    ['# ', 'heading', 'H1'],
    ['- ', 'bullet_list', 'bullet'],
    ['1. ', 'ordered_list', 'ordered'],
    ['> ', 'blockquote', 'quote'],
  ])('"%s" turns the paragraph into %s', (prefix, nodeName) => {
    load('')
    placeCursorAtEndOfFirstBlock()
    type(prefix)
    expect(view().state.doc.firstChild?.type.name).toBe(nodeName)
  })

  it('"- [ ] " creates a task item', () => {
    load('')
    placeCursorAtEndOfFirstBlock()
    type('- [ ] ')
    const list = view().state.doc.firstChild!
    expect(list.type.name).toBe('bullet_list')
    expect(list.firstChild?.attrs['checked']).toBe(false)
  })

  it('"**bold**" becomes a strong mark', () => {
    load('')
    placeCursorAtEndOfFirstBlock()
    type('**bold**')
    expect(editor.crepe.getMarkdown().trim()).toBe('**bold**')
  })

  it('"---" becomes a horizontal rule', () => {
    load('')
    placeCursorAtEndOfFirstBlock()
    type('---')
    expect(editor.crepe.getMarkdown()).toContain('---')
    expect(view().state.doc.content.child(0).type.name).toBe('hr')
  })

  it('"```" followed by a space becomes a code block (Milkdown rule needs the trailing whitespace)', () => {
    load('')
    placeCursorAtEndOfFirstBlock()
    type('``` ')
    const names = [] as string[]
    view().state.doc.forEach((n) => names.push(n.type.name))
    expect(names).toContain('code_block')
  })
})

describe('checklist (F-EDIT-09)', () => {
  it('toolbar task command serializes as "- [ ]" and toggling checked gives "- [x]"', () => {
    load('todo')
    placeCursorAtEndOfFirstBlock()
    cmd.setList(target, 'task')
    expect(editor.crepe.getMarkdown().trim()).toBe('- [ ] todo')

    const v = view()
    v.state.doc.descendants((node, pos) => {
      if (node.type.name === 'list_item') {
        v.dispatch(v.state.tr.setNodeMarkup(pos, undefined, { ...node.attrs, checked: true }))
        return false
      }
      return true
    })
    expect(editor.crepe.getMarkdown().trim()).toBe('- [x] todo')
  })

  it('task → bullet keeps the item and drops the checkbox', () => {
    load('- [ ] todo')
    placeCursorAtEndOfFirstBlock()
    const v = view()
    v.dispatch(v.state.tr.setSelection(TextSelection.create(v.state.doc, 3)))
    cmd.setList(target, 'bullet')
    expect(editor.crepe.getMarkdown().trim()).toBe('- todo')
  })
})

describe('find/replace (F-EDIT-11)', () => {
  it('matches across inline mark boundaries and inside code blocks', () => {
    load('foo **bar** baz\n\n```js\nfoobar\n```\n')
    const matches = findMatches(view().state.doc, { query: 'foo bar', caseSensitive: false, wholeWord: false })
    expect(matches).toHaveLength(1)
    const all = findMatches(view().state.doc, { query: 'foo', caseSensitive: false, wholeWord: false })
    expect(all).toHaveLength(2)
  })

  it('whole-word and case options', () => {
    load('Cat cat catalog 고양이 고양이는')
    const doc = view().state.doc
    expect(findMatches(doc, { query: 'cat', caseSensitive: false, wholeWord: false })).toHaveLength(3)
    expect(findMatches(doc, { query: 'cat', caseSensitive: true, wholeWord: false })).toHaveLength(2)
    expect(findMatches(doc, { query: 'cat', caseSensitive: false, wholeWord: true })).toHaveLength(2)
    expect(findMatches(doc, { query: '고양이', caseSensitive: false, wholeWord: true })).toHaveLength(1)
  })

  it('replaceAll is a single undo step', () => {
    load('a b a b a')
    const v = view()
    findSet(v, { query: 'a', caseSensitive: false, wholeWord: true })
    expect(findReplaceAll(v, 'x')).toBe(3)
    expect(editor.crepe.getMarkdown().trim()).toBe('x b x b x')
    cmd.undo(target)
    expect(editor.crepe.getMarkdown().trim()).toBe('a b a b a')
  })

  it('next/prev cycle through matches', () => {
    load('one two one two one')
    const v = view()
    const s = findSet(v, { query: 'one', caseSensitive: false, wholeWord: false })
    expect(s.matches).toHaveLength(3)
    expect(s.index).toBe(0)
    expect(findStep(v, 1).index).toBe(1)
    expect(findStep(v, 1).index).toBe(2)
    expect(findStep(v, 1).index).toBe(0)
    expect(findStep(v, -1).index).toBe(2)
  })
})
