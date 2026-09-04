import { describe, expect, it } from 'vitest'
import { align, canonical, parseBlocks, roundTrip } from '../src/roundtrip'
import { joinFrontMatter, splitFrontMatter } from '../src/frontmatter'

describe('canonical()', () => {
  it('ignores emphasis marker style', () => {
    const a = parseBlocks('some *emph* and __strong__ text')[0]!
    const b = parseBlocks('some _emph_ and **strong** text')[0]!
    expect(a.key).toBe(b.key)
  })

  it('ignores bullet marker style but keeps list content', () => {
    const a = parseBlocks('- one\n- two')[0]!
    const b = parseBlocks('* one\n* two')[0]!
    const c = parseBlocks('- one\n- three')[0]!
    expect(a.key).toBe(b.key)
    expect(a.key).not.toBe(c.key)
  })

  it('ignores table alignment padding', () => {
    const a = parseBlocks('| a | b |\n|---|---|\n| 1 | 2 |')[0]!
    const b = parseBlocks('| a    | b |\n| ---- | - |\n| 1    | 2 |')[0]!
    expect(a.key).toBe(b.key)
  })

  it('keeps code block content exact', () => {
    const a = parseBlocks('```js\nlet a = 1\n```')[0]!
    const b = parseBlocks('```js\nlet a = 2\n```')[0]!
    expect(a.key).not.toBe(b.key)
  })

  it('collapses whitespace inside text', () => {
    expect(canonical({ type: 'text', value: 'a  \n b' })).toBe(canonical({ type: 'text', value: 'a b' }))
  })
})

describe('align()', () => {
  it('produces LCS alignment', () => {
    const items = align(['a', 'b', 'c'], ['a', 'x', 'c'])
    expect(items).toEqual([{ a: 0, b: 0 }, { a: 1 }, { b: 1 }, { a: 2, b: 2 }])
  })
})

describe('roundTrip()', () => {
  const original = [
    '# Title',
    '',
    'Some *text* with __strong__ words.',
    '',
    '',
    '* item one',
    '* item two',
    '',
    '| a | b |',
    '|---|---|',
    '| 1 | 2 |',
    '',
    '```js',
    'const x = 1',
    '```',
    '',
  ].join('\n')

  it('returns the original verbatim when nothing changed (even with restyled serialization)', () => {
    const restyled = original.replace('__strong__', '**strong**').replace(/\* item/g, '- item')
    const r = roundTrip(original, restyled)
    expect(r.text).toBe(original)
    expect(r.changedBlocks).toEqual([])
  })

  it('replaces only the edited block and keeps original spacing around untouched blocks', () => {
    const edited = original.replace('# Title', '# New Title').replace(/\* item/g, '- item')
    const r = roundTrip(original, edited)
    expect(r.changedBlocks).toEqual([0])
    expect(r.text).toBe(original.replace('# Title', '# New Title'))
  })

  it('inserts a new block with a blank-line separator', () => {
    const edited = original.replace('# Title\n', '# Title\n\nIntro paragraph.\n')
    const r = roundTrip(original, edited)
    expect(r.changedBlocks).toEqual([1])
    expect(r.text).toContain('# Title\n\nIntro paragraph.\n\nSome *text*')
    expect(r.text.endsWith('```\n')).toBe(true)
  })

  it('drops deleted blocks', () => {
    const edited = original.replace('| a | b |\n|---|---|\n| 1 | 2 |\n\n', '')
    const r = roundTrip(original, edited)
    expect(r.text).not.toContain('| a | b |')
    expect(r.text).toContain('* item two\n\n```js')
  })

  it('handles empty documents', () => {
    expect(roundTrip('', 'hello').text).toBe('hello')
    expect(roundTrip('hello', '').text).toBe('')
  })

  it('keeps missing trailing newline when the last block is untouched', () => {
    const src = '# A\n\ntext'
    expect(roundTrip(src, '# A\n\ntext\n').text).toBe(src)
  })
})

describe('front matter split/join', () => {
  it('splits a yaml block and joins it back byte-exact', () => {
    const doc = '---\ntitle: x\n# comment\ntags: [a, b]\n---\n\n# Body\n'
    const { raw, body } = splitFrontMatter(doc)
    expect(raw).toBe('---\ntitle: x\n# comment\ntags: [a, b]\n---\n')
    expect(body).toBe('\n# Body\n')
    expect(joinFrontMatter(raw, body)).toBe(doc)
  })

  it('handles empty front matter and documents without one', () => {
    expect(splitFrontMatter('---\n---\nbody').raw).toBe('---\n---\n')
    expect(splitFrontMatter('# no fm\n---\n').raw).toBe('')
    expect(splitFrontMatter('---\nunterminated').raw).toBe('')
  })
})
