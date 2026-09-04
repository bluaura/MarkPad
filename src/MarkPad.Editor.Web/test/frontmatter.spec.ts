import { describe, expect, it } from 'vitest'
import {
  addChildMap,
  addKey,
  coerce,
  deleteKey,
  moveKey,
  parseFrontMatter,
  renameKey,
  setSeqItems,
  setValue,
  summarize,
} from '../src/plugins/frontmatter/model'

const RAW = [
  '# blog post metadata',
  'title: "Hello"   # quoted on purpose',
  'date: 2026-09-04',
  'draft: false',
  'tags: [a, b]',
  'author:',
  '  name: Lee',
  '  links:',
  '    - kind: web',
  '      url: https://x',
  '',
].join('\n')

describe('front matter model (T-49)', () => {
  it('parses into a typed tree', () => {
    const { tree, fallbackReason } = parseFrontMatter(RAW)
    expect(fallbackReason).toBeNull()
    expect(tree.map((n) => n.key)).toEqual(['title', 'date', 'draft', 'tags', 'author'])
    expect(tree[0]).toMatchObject({ kind: 'scalar', scalarType: 'string', value: 'Hello' })
    expect(tree[1]).toMatchObject({ kind: 'scalar', scalarType: 'date' })
    expect(tree[2]).toMatchObject({ kind: 'scalar', scalarType: 'boolean', value: false })
    expect(tree[3]).toMatchObject({ kind: 'seq', items: ['a', 'b'] })
    expect(tree[4]!.kind).toBe('map')
    expect(tree[4]!.children![1]!.kind).toBe('seq')
    expect(tree[4]!.children![1]!.children![0]!.kind).toBe('map')
    expect(summarize(tree)).toBe('title: Hello · date: 2026-09-04 · draft: false · +2')
  })

  it('edits preserve comments, order and quoting', () => {
    const { doc } = parseFrontMatter(RAW)
    let raw = setValue(doc, ['title'], 'World')
    expect(raw).toContain('# blog post metadata')
    expect(raw).toMatch(/title: "World" +# quoted on purpose/) // quoting + comment kept (spacing normalized by yaml)
    raw = setValue(doc, ['author', 'links', 0, 'url'], 'https://y')
    expect(raw).toContain('      url: https://y')
    raw = setValue(doc, ['draft'], true)
    expect(raw).toContain('draft: true')
    expect(raw.indexOf('title:')).toBeLessThan(raw.indexOf('date:'))
  })

  it('supports add / rename / delete / move / seq items', () => {
    const { doc } = parseFrontMatter(RAW)
    let raw = addKey(doc, [], 'layout', 'post')
    expect(raw.trimEnd().endsWith('layout: post')).toBe(true)
    raw = renameKey(doc, ['draft'], 'published')
    expect(raw).toContain('published: false')
    expect(raw).not.toContain('draft:')
    raw = moveKey(doc, ['published'], -1)
    expect(raw.indexOf('published:')).toBeLessThan(raw.indexOf('date:'))
    raw = setSeqItems(doc, ['tags'], ['x', 'y', 'z'])
    expect(raw).toMatch(/tags: \[ ?x, y, z ?\]/)
    raw = addChildMap(doc, ['author'], 'social')
    raw = setValue(doc, ['author', 'social', 'x'], '@lee')
    expect(raw).toContain('  social:\n    x: "@lee"')
    raw = deleteKey(doc, ['author', 'links'])
    expect(raw).not.toContain('links:')
  })

  it('falls back for anchors, aliases, tags and multi-doc', () => {
    expect(parseFrontMatter('a: &x 1\nb: *x\n').fallbackReason).toContain('앵커')
    expect(parseFrontMatter('a: !custom 1\n').fallbackReason).toContain('태그')
    expect(parseFrontMatter('a: 1\n---\nb: 2\n').fallbackReason).toContain('다중 문서')
    expect(parseFrontMatter('a: [1\n').fallbackReason).toContain('파싱 오류')
    expect(parseFrontMatter('').fallbackReason).toBeNull()
  })

  it('coerces typed text', () => {
    expect(coerce('42')).toBe(42)
    expect(coerce('true')).toBe(true)
    expect(coerce('~')).toBeNull()
    expect(coerce('hello world')).toBe('hello world')
    expect(coerce('2026-09-04')).toBe('2026-09-04')
  })
})
