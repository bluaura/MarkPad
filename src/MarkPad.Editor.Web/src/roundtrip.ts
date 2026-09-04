/**
 * Round-trip preservation (ARCHITECTURE.md §5, PRD §5.3).
 *
 * Both the original text and the editor's re-serialized text are parsed into top-level
 * mdast blocks. Blocks whose canonical form is identical are emitted from the ORIGINAL
 * byte range, so untouched content survives with zero diff. Only blocks that were
 * added or changed are emitted from the editor's serialization.
 *
 * canonical() deliberately mirrors what a ProseMirror document can represent:
 *  - inline content is flattened into "mark runs" (text + sorted set of marks) so
 *    `[_a_](u)` and `_[a](u)_` compare equal;
 *  - link/image references are resolved against the document's definitions
 *    (the editor inlines them);
 *  - inline HTML is normalized (`<br >` ≡ `<br />`), and `<br />`-only table cells count as empty;
 *  - text whitespace is collapsed.
 * `definition` blocks can never come back from the editor, so they are always kept from the original.
 */
import remarkFrontmatter from 'remark-frontmatter'
import remarkGfm from 'remark-gfm'
import remarkMath from 'remark-math'
import remarkParse from 'remark-parse'
import { unified } from 'unified'
import type { Definition, Root, RootContent } from 'mdast'

export interface Block {
  node: RootContent
  start: number
  end: number
  key: string
}

export interface RoundTripResult {
  text: string
  changedBlocks: number[]
}

const processor = unified().use(remarkParse).use(remarkGfm).use(remarkFrontmatter, ['yaml']).use(remarkMath)

type Definitions = Map<string, { url: string; title?: string | null }>

interface AnyNode {
  type?: string
  value?: unknown
  children?: AnyNode[]
  [k: string]: unknown
}

export interface ParsedDoc {
  blocks: Block[]
  defs: Definitions
}

export function parseDoc(text: string): ParsedDoc {
  const root = processor.parse(text) as Root
  const defs = collectDefinitions(root)
  const blocks = root.children.map((node) => ({
    node,
    start: node.position?.start.offset ?? 0,
    end: node.position?.end.offset ?? 0,
    key: canonical(node, defs),
  }))
  return { blocks, defs }
}

export function parseBlocks(text: string): Block[] {
  return parseDoc(text).blocks
}

function collectDefinitions(root: Root): Definitions {
  const defs: Definitions = new Map()
  const walk = (n: AnyNode): void => {
    if (n.type === 'definition') {
      const d = n as unknown as Definition
      const id = d.identifier.toLowerCase()
      if (!defs.has(id)) defs.set(id, { url: d.url, title: d.title })
    }
    n.children?.forEach(walk)
  }
  walk(root as unknown as AnyNode)
  return defs
}

/** Canonical form of a node as a stable JSON string. */
export function canonical(node: unknown, defs: Definitions = new Map()): string {
  return JSON.stringify(strip(node as AnyNode, defs))
}

const PHRASING_PARENTS = new Set(['paragraph', 'heading', 'tableCell'])
const INLINE_MARKS = new Set(['emphasis', 'strong', 'delete', 'link', 'linkReference'])

function normalizeHtml(value: string): string {
  return value
    .replace(/\s+/g, ' ')
    .replace(/\s*\/?>/g, '>')
    .replace(/\s+</g, '<')
    .trim()
}

function strip(value: unknown, defs: Definitions): unknown {
  if (Array.isArray(value)) return value.map((v) => strip(v, defs))
  if (!value || typeof value !== 'object') return value
  const node = value as AnyNode
  const type = node.type

  if (type && PHRASING_PARENTS.has(type)) {
    const runs = flattenInline(node.children ?? [], defs, [])
    const cleaned = type === 'tableCell' ? dropBrOnly(runs) : runs
    const out: Record<string, unknown> = { type, runs: cleaned }
    if (type === 'heading') out['depth'] = node['depth']
    return out
  }

  if (type === 'table') return stripTable(node, defs)

  const out: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(node)) {
    if (k === 'position' || k === 'data') continue
    // Loose/tight list layout is a serialization detail the editor cannot express; ignore it.
    if (k === 'spread' && (type === 'list' || type === 'listItem')) continue
    if (k === 'value' && type === 'html' && typeof v === 'string') {
      out[k] = normalizeHtml(v)
    } else if (k === 'value' && type === 'text' && typeof v === 'string') {
      out[k] = v.replace(/\s+/g, ' ')
    } else {
      out[k] = strip(v, defs)
    }
  }
  return out
}

/**
 * GFM lets rows have more or fewer cells than the header; ProseMirror tables are rectangular, so the
 * editor pads every row to the widest one. Canonical form: drop trailing empty cells per row and trim
 * `align` to the widest remaining row.
 */
function stripTable(node: AnyNode, defs: Definitions): unknown {
  const rows = (node.children ?? []).map((row) => {
    const cells = (row.children ?? []).map((cell) => strip(cell, defs) as { type: string; runs: Run[] })
    let end = cells.length
    while (end > 0 && (cells[end - 1]?.runs.length ?? 0) === 0) end--
    return { type: 'tableRow', children: cells.slice(0, end) }
  })
  const width = rows.reduce((max, r) => Math.max(max, r.children.length), 0)
  const align = Array.isArray(node['align']) ? (node['align'] as unknown[]) : []
  const normAlign = Array.from({ length: width }, (_, i) => align[i] ?? null)
  return { type: 'table', align: normAlign, children: rows }
}

interface Run {
  t?: string // text
  n?: Record<string, unknown> // atom node (image, inlineCode, break, html, footnoteReference, inlineMath)
  m: string[] // sorted marks
}

function markKey(node: AnyNode, defs: Definitions): string {
  switch (node.type) {
    case 'link':
      return `link:${String(node['url'])}:${node['title'] ? String(node['title']) : ''}`
    case 'linkReference': {
      const d = defs.get(String(node['identifier']).toLowerCase())
      return d ? `link:${d.url}:${d.title ?? ''}` : `linkref:${String(node['identifier'])}`
    }
    default:
      return String(node.type)
  }
}

function flattenInline(children: AnyNode[], defs: Definitions, marks: string[]): Run[] {
  const runs: Run[] = []
  const push = (run: Run): void => {
    const prev = runs[runs.length - 1]
    if (run.t !== undefined && prev?.t !== undefined && sameMarks(prev.m, run.m)) {
      prev.t += run.t
    } else {
      runs.push(run)
    }
  }
  for (const child of children) {
    const type = child.type ?? ''
    if (INLINE_MARKS.has(type)) {
      const next = [...marks, markKey(child, defs)].sort()
      for (const r of flattenInline(child.children ?? [], defs, next)) push(r)
      continue
    }
    switch (type) {
      case 'text':
        push({ t: String(child.value ?? ''), m: marks })
        break
      case 'image':
        push({ n: { type, url: child['url'], alt: child['alt'] ?? '', title: child['title'] ?? null }, m: marks })
        break
      case 'imageReference': {
        const d = defs.get(String(child['identifier']).toLowerCase())
        push({
          n: { type: 'image', url: d?.url ?? `ref:${String(child['identifier'])}`, alt: child['alt'] ?? '', title: d?.title ?? null },
          m: marks,
        })
        break
      }
      case 'inlineCode':
      case 'inlineMath':
        push({ n: { type, value: String(child.value ?? '').replace(/\s+/g, ' ') }, m: marks })
        break
      case 'html':
        push({ n: { type, value: normalizeHtml(String(child.value ?? '')) }, m: marks })
        break
      case 'break':
        push({ n: { type }, m: marks })
        break
      case 'footnoteReference':
        push({ n: { type, id: String(child['identifier']).toLowerCase() }, m: marks })
        break
      default:
        push({ n: strip(child, defs) as Record<string, unknown>, m: marks })
    }
  }
  return normalizeRuns(runs)
}

/**
 * Whitespace at the edge of a marked run is moved out of the marks (`[a *b* c](u)` and
 * `*[b](u)* [c](u)` express the same ProseMirror content), inner whitespace is collapsed,
 * empty runs are dropped and adjacent runs with identical marks are merged.
 */
function normalizeRuns(runs: Run[]): Run[] {
  const split: Run[] = []
  for (const r of runs) {
    if (r.t === undefined || r.m.length === 0) {
      split.push(r)
      continue
    }
    const lead = /^\s+/.exec(r.t)?.[0] ?? ''
    const trail = /\s+$/.exec(r.t)?.[0] ?? ''
    const core = r.t.slice(lead.length, r.t.length - trail.length)
    if (lead) split.push({ t: ' ', m: [] })
    if (core) split.push({ t: core, m: r.m })
    if (trail) split.push({ t: ' ', m: [] })
  }
  const merged: Run[] = []
  for (const r of split) {
    const prev = merged[merged.length - 1]
    if (r.t !== undefined && prev?.t !== undefined && sameMarks(prev.m, r.m)) prev.t += r.t
    else merged.push({ ...r })
  }
  return merged
    .map((r) => (r.t !== undefined ? { ...r, t: r.t.replace(/\s+/g, ' ') } : r))
    .filter((r) => r.t === undefined || r.t.length > 0)
}

function sameMarks(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((x, i) => x === b[i])
}

function dropBrOnly(runs: Run[]): Run[] {
  const meaningful = runs.filter((r) => !(r.n?.['type'] === 'html' && r.n['value'] === '<br>') && !(r.t !== undefined && r.t.trim() === ''))
  return meaningful
}

/** One entry of the alignment: matched (a & b), inserted (b only) or deleted (a only). */
export interface AlignItem {
  a?: number
  b?: number
}

/** Longest-common-subsequence alignment of two key sequences. */
export function align(aKeys: readonly string[], bKeys: readonly string[]): AlignItem[] {
  const n = aKeys.length
  const m = bKeys.length
  const width = m + 1
  const dp = new Uint32Array((n + 1) * width)
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i * width + j] =
        aKeys[i] === bKeys[j]
          ? (dp[(i + 1) * width + j + 1] ?? 0) + 1
          : Math.max(dp[(i + 1) * width + j] ?? 0, dp[i * width + j + 1] ?? 0)
    }
  }
  const items: AlignItem[] = []
  let i = 0
  let j = 0
  while (i < n && j < m) {
    if (aKeys[i] === bKeys[j]) {
      items.push({ a: i, b: j })
      i++
      j++
    } else if ((dp[(i + 1) * width + j] ?? 0) >= (dp[i * width + j + 1] ?? 0)) {
      items.push({ a: i })
      i++
    } else {
      items.push({ b: j })
      j++
    }
  }
  while (i < n) items.push({ a: i++ })
  while (j < m) items.push({ b: j++ })
  return items
}

/** Blocks the editor cannot express; when missing from the editor output they are kept verbatim. */
function isKeepAlways(node: RootContent): boolean {
  return node.type === 'definition'
}

interface ChildBlock {
  start: number
  end: number
  key: string
}

/**
 * Children with byte ranges. Blockquote children start after the `> ` prefix in mdast, so their range is
 * widened to the line start (the prefix of continuation lines is already inside the raw slice).
 */
function childBlocks(node: AnyNode, defs: Definitions, text: string): ChildBlock[] {
  const widen = node.type === 'blockquote'
  return (node.children ?? []).map((c) => {
    const pos = (c as { position?: { start: { offset?: number }; end: { offset?: number } } }).position
    let start = pos?.start.offset ?? 0
    if (widen) start = text.lastIndexOf('\n', start - 1) + 1
    return { start, end: pos?.end.offset ?? 0, key: canonical(c, defs) }
  })
}

/** `- a` vs `* a` would start a new list (CommonMark), so items taken from the editor adopt the original's marker. */
function harmonizeMarker(slice: string, template: string, ordered: boolean): string {
  if (ordered) {
    const delim = /^\s*\d+([.)])/.exec(template)?.[1]
    return delim ? slice.replace(/^(\s*\d+)[.)]/, `$1${delim}`) : slice
  }
  const marker = /^\s*([-*+])/.exec(template)?.[1]
  return marker ? slice.replace(/^(\s*)[-*+]/, `$1${marker}`) : slice
}

/**
 * T-34: a list or blockquote that changed is re-assembled from its children so that untouched items keep
 * their original bytes (one level deep; ADR-03). Returns null when the blocks are not comparable.
 */
function mergeBlock(a: Block, b: Block, original: string, current: string, defsA: Definitions, defsB: Definitions): string | null {
  const an = a.node as unknown as AnyNode
  const bn = b.node as unknown as AnyNode
  if (an.type !== bn.type || (an.type !== 'list' && an.type !== 'blockquote')) return null
  if (an.type === 'list' && an['ordered'] !== bn['ordered']) return null
  const ac = childBlocks(an, defsA, original)
  const bc = childBlocks(bn, defsB, current)
  if (ac.length === 0 || bc.length === 0) return null
  const items = align(
    ac.map((c) => c.key),
    bc.map((c) => c.key),
  )
  if (!items.some((it) => it.a !== undefined && it.b !== undefined)) return null

  const ordered = an.type === 'list' && an['ordered'] === true
  const template = original.slice(ac[0]!.start, ac[0]!.end)
  const parts: string[] = []
  let prev: { side: 'a' | 'b'; index: number } | null = null
  const gapBefore = (side: 'a' | 'b', index: number): string => {
    if (!prev) return ''
    if (side === 'a') {
      if (prev.side === 'a' && index === prev.index + 1) return original.slice(ac[prev.index]!.end, ac[index]!.start)
      if (index > 0) return original.slice(ac[index - 1]!.end, ac[index]!.start)
      return '\n'
    }
    if (index > 0) return current.slice(bc[index - 1]!.end, bc[index]!.start)
    return '\n'
  }
  for (const it of items) {
    if (it.a !== undefined && it.b !== undefined) {
      parts.push(gapBefore('a', it.a), original.slice(ac[it.a]!.start, ac[it.a]!.end))
      prev = { side: 'a', index: it.a }
    } else if (it.b !== undefined) {
      const slice = current.slice(bc[it.b]!.start, bc[it.b]!.end)
      parts.push(gapBefore('b', it.b), an.type === 'list' ? harmonizeMarker(slice, template, ordered) : slice)
      prev = { side: 'b', index: it.b }
    }
  }
  return parts.join('')
}

interface Piece {
  text: string
  aIndex?: number
}

/**
 * Assemble the output text. `original` and `current` are LF-normalized markdown WITHOUT
 * front matter (see frontmatter.ts). Trailing whitespace of the original file is kept when
 * the last block is untouched; otherwise a single newline is emitted and the host applies
 * its end-of-file policy.
 */
export function roundTrip(original: string, current: string): RoundTripResult {
  const docA = parseDoc(original)
  const docB = parseDoc(current)
  const a = docA.blocks
  const b = docB.blocks
  if (a.length === 0) return { text: current, changedBlocks: b.map((_, i) => i) }
  if (b.length === 0) return { text: '', changedBlocks: [] }

  const items = align(
    a.map((x) => x.key),
    b.map((x) => x.key),
  )

  const pieces: Piece[] = []
  const changedBlocks: number[] = []

  const flushRegion = (aOnly: number[], bOnly: number[]): void => {
    let k = 0
    const usedA = new Set<number>()
    for (; k < aOnly.length && k < bOnly.length; k++) {
      const ai = aOnly[k]!
      const bi = bOnly[k]!
      const merged = mergeBlock(a[ai]!, b[bi]!, original, current, docA.defs, docB.defs)
      if (merged === null) break
      pieces.push({ text: merged, aIndex: ai })
      changedBlocks.push(bi)
      usedA.add(ai)
    }
    for (const ai of aOnly) {
      if (!usedA.has(ai) && isKeepAlways(a[ai]!.node)) pieces.push({ text: original.slice(a[ai]!.start, a[ai]!.end), aIndex: ai })
    }
    for (let m = k; m < bOnly.length; m++) {
      const bi = bOnly[m]!
      pieces.push({ text: current.slice(b[bi]!.start, b[bi]!.end) })
      changedBlocks.push(bi)
    }
  }

  let aOnly: number[] = []
  let bOnly: number[] = []
  for (const item of items) {
    if (item.a !== undefined && item.b !== undefined) {
      flushRegion(aOnly, bOnly)
      aOnly = []
      bOnly = []
      pieces.push({ text: original.slice(a[item.a]!.start, a[item.a]!.end), aIndex: item.a })
    } else if (item.a !== undefined) {
      aOnly.push(item.a)
    } else if (item.b !== undefined) {
      bOnly.push(item.b)
    }
  }
  flushRegion(aOnly, bOnly)

  const parts: string[] = []
  for (let i = 0; i < pieces.length; i++) {
    const piece = pieces[i]!
    const prev = pieces[i - 1]
    if (i === 0) {
      if (piece.aIndex === 0) parts.push(original.slice(0, a[0]!.start))
    } else if (prev?.aIndex !== undefined && piece.aIndex !== undefined && piece.aIndex === prev.aIndex + 1) {
      parts.push(original.slice(a[prev.aIndex]!.end, a[piece.aIndex]!.start))
    } else {
      parts.push('\n\n')
    }
    parts.push(piece.text)
  }

  const last = pieces[pieces.length - 1]
  if (last?.aIndex === a.length - 1) {
    parts.push(original.slice(a[last.aIndex]!.end))
  } else if (pieces.length > 0) {
    parts.push('\n')
  }

  changedBlocks.sort((x, y) => x - y)
  return { text: parts.join(''), changedBlocks }
}
