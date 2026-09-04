/**
 * Round-trip preservation (ARCHITECTURE.md §5, PRD §5.3).
 *
 * Both the original text and the editor's re-serialized text are parsed into top-level
 * mdast blocks. Blocks whose canonical form is identical are emitted from the ORIGINAL
 * byte range, so untouched content survives with zero diff. Only blocks that were
 * added or changed are emitted from the editor's serialization.
 */
import remarkFrontmatter from 'remark-frontmatter'
import remarkGfm from 'remark-gfm'
import remarkMath from 'remark-math'
import remarkParse from 'remark-parse'
import { unified } from 'unified'
import type { Root, RootContent } from 'mdast'

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

export function parseBlocks(text: string): Block[] {
  const root = processor.parse(text) as Root
  return root.children.map((node) => ({
    node,
    start: node.position?.start.offset ?? 0,
    end: node.position?.end.offset ?? 0,
    key: canonical(node),
  }))
}

/**
 * Canonical form of a node: positions dropped, adjacent text merged, whitespace inside
 * text collapsed. Everything else (structure, attributes, code/html values) is kept so that
 * a real content change always produces a different key.
 */
export function canonical(node: unknown): string {
  return JSON.stringify(strip(node))
}

interface AnyNode {
  type?: string
  value?: unknown
  children?: AnyNode[]
  [k: string]: unknown
}

function strip(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(strip)
  if (value && typeof value === 'object') {
    const node = value as AnyNode
    const out: Record<string, unknown> = {}
    for (const [k, v] of Object.entries(node)) {
      if (k === 'position') continue
      if (k === 'children' && Array.isArray(v)) {
        out[k] = mergeText(v as AnyNode[]).map(strip)
      } else if (k === 'value' && node.type === 'text' && typeof v === 'string') {
        out[k] = v.replace(/\s+/g, ' ').trim()
      } else {
        out[k] = strip(v)
      }
    }
    return out
  }
  return value
}

function mergeText(children: AnyNode[]): AnyNode[] {
  const out: AnyNode[] = []
  for (const child of children) {
    const prev = out[out.length - 1]
    if (child.type === 'text' && prev?.type === 'text') {
      out[out.length - 1] = { ...prev, value: String(prev.value) + String(child.value) }
    } else {
      out.push(child)
    }
  }
  return out
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

/**
 * Assemble the output text. `original` and `current` are LF-normalized markdown WITHOUT
 * front matter (see frontmatter.ts). Trailing whitespace of the original file is kept when
 * the last block is untouched; otherwise a single newline is emitted and the host applies
 * its end-of-file policy.
 */
export function roundTrip(original: string, current: string): RoundTripResult {
  const a = parseBlocks(original)
  const b = parseBlocks(current)
  if (a.length === 0) return { text: current, changedBlocks: b.map((_, i) => i) }
  if (b.length === 0) return { text: '', changedBlocks: [] }

  const items = align(
    a.map((x) => x.key),
    b.map((x) => x.key),
  )

  const parts: string[] = []
  const changedBlocks: number[] = []
  let lastMatchedA: number | null = null
  let emitted = 0

  for (const item of items) {
    if (item.a !== undefined && item.b !== undefined) {
      const blk = a[item.a]!
      if (emitted === 0) {
        if (item.a === 0) parts.push(original.slice(0, blk.start))
      } else if (lastMatchedA === item.a - 1) {
        parts.push(original.slice(a[lastMatchedA]!.end, blk.start))
      } else {
        parts.push('\n\n')
      }
      parts.push(original.slice(blk.start, blk.end))
      lastMatchedA = item.a
      emitted++
    } else if (item.b !== undefined) {
      const blk = b[item.b]!
      if (emitted > 0) parts.push('\n\n')
      parts.push(current.slice(blk.start, blk.end))
      changedBlocks.push(item.b)
      lastMatchedA = null
      emitted++
    } else {
      lastMatchedA = null
    }
  }

  if (lastMatchedA === a.length - 1) {
    parts.push(original.slice(a[lastMatchedA]!.end))
  } else if (emitted > 0) {
    parts.push('\n')
  }

  return { text: parts.join(''), changedBlocks }
}
