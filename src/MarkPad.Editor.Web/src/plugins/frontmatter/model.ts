/**
 * YAML front matter editing model (PRD F-EDIT-15, T-49). All edits go through the `yaml` Document API so
 * comments, key order, indentation and quoting styles survive (ADR-04). Anything the GUI cannot represent
 * (anchors/aliases, custom tags, multiple documents, parse errors) reports a fallback reason → raw editing.
 */
import {
  Document,
  Pair,
  Scalar,
  YAMLMap,
  YAMLSeq,
  isAlias,
  isMap,
  isPair,
  isScalar,
  isSeq,
  parseAllDocuments,
  parseDocument,
  type Node as YamlNode,
} from 'yaml'

export type FmPath = (string | number)[]
export type FmScalarType = 'string' | 'number' | 'boolean' | 'date' | 'null'

export interface FmNode {
  path: FmPath
  key: string | number
  kind: 'map' | 'seq' | 'scalar'
  scalarType?: FmScalarType
  value?: unknown
  /** For sequences of scalars: the chips. */
  items?: unknown[]
  children?: FmNode[]
}

export interface ParsedFrontMatter {
  doc: Document
  tree: FmNode[]
  fallbackReason: string | null
}

const DATE_RE = /^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2})?)?$/

export function scalarTypeOf(value: unknown): FmScalarType {
  if (value === null || value === undefined) return 'null'
  if (typeof value === 'boolean') return 'boolean'
  if (typeof value === 'number') return 'number'
  if (value instanceof Date) return 'date'
  if (typeof value === 'string' && DATE_RE.test(value)) return 'date'
  return 'string'
}

/** Text typed by the user → YAML value (numbers/booleans/null auto-detected, otherwise string). */
export function coerce(text: string): unknown {
  const t = text.trim()
  if (t === '') return ''
  if (t === 'true') return true
  if (t === 'false') return false
  if (t === 'null' || t === '~') return null
  if (/^-?\d+(\.\d+)?$/.test(t)) return Number(t)
  return text
}

function unsupported(node: unknown, reasons: Set<string>): void {
  if (!node || typeof node !== 'object') return
  if (isAlias(node)) reasons.add('별칭(*alias)')
  const n = node as YamlNode & { anchor?: string; tag?: string }
  if (n.anchor) reasons.add('앵커(&anchor)')
  if (n.tag && !n.tag.startsWith('tag:yaml.org,2002:')) reasons.add(`태그(${n.tag})`)
  if (isMap(node)) for (const p of node.items) { unsupported(p.key, reasons); unsupported(p.value, reasons) }
  else if (isSeq(node)) for (const it of node.items) unsupported(it, reasons)
}

function scalarValue(node: unknown): unknown {
  return isScalar(node) ? node.value : node
}

function buildNode(key: string | number, value: unknown, path: FmPath): FmNode {
  if (isMap(value)) {
    return { path, key, kind: 'map', children: buildChildren(value, path) }
  }
  if (isSeq(value)) {
    const allScalar = value.items.every((it) => isScalar(it) || it === null || typeof it !== 'object')
    if (allScalar) return { path, key, kind: 'seq', items: value.items.map(scalarValue) }
    return {
      path,
      key,
      kind: 'seq',
      children: value.items.map((it, i) => buildNode(i, it, [...path, i])),
    }
  }
  const v = scalarValue(value)
  return { path, key, kind: 'scalar', scalarType: scalarTypeOf(v), value: v }
}

function buildChildren(map: YAMLMap, path: FmPath): FmNode[] {
  return map.items.map((pair) => {
    const key = String(scalarValue(pair.key))
    return buildNode(key, pair.value, [...path, key])
  })
}

export function parseFrontMatter(raw: string): ParsedFrontMatter {
  const reasons = new Set<string>()
  let doc: Document
  try {
    const all = parseAllDocuments(raw)
    if (all.length > 1) reasons.add('다중 문서(---)')
    doc = all[0] ?? parseDocument(raw)
  } catch (err) {
    doc = parseDocument('')
    reasons.add(`파싱 오류: ${String(err)}`)
  }
  if (doc.errors.length > 0) reasons.add(`파싱 오류: ${doc.errors[0]!.message.split('\n')[0]}`)
  unsupported(doc.contents, reasons)
  let tree: FmNode[] = []
  if (isMap(doc.contents)) tree = buildChildren(doc.contents, [])
  else if (doc.contents != null && raw.trim().length > 0) reasons.add('최상위가 맵이 아님')
  return { doc, tree, fallbackReason: reasons.size ? Array.from(reasons).join(', ') : null }
}

/** Collapsed header text: top N keys as `key: value` (PRD F-VIEW-07). */
export function summarize(tree: FmNode[], max = 3): string {
  const parts = tree.slice(0, max).map((n) => {
    if (n.kind === 'scalar') return `${n.key}: ${formatScalar(n.value)}`
    if (n.kind === 'seq' && n.items) return `${n.key}: ${n.items.slice(0, 4).map(formatScalar).join(', ')}${n.items.length > 4 ? '…' : ''}`
    return `${n.key}: {…}`
  })
  if (tree.length > max) parts.push(`+${tree.length - max}`)
  return parts.join(' · ')
}

export function formatScalar(v: unknown): string {
  if (v === null || v === undefined) return '~'
  if (v instanceof Date) return v.toISOString().slice(0, 10)
  return String(v)
}

// ---- mutations (return the new raw text) ----

export function setValue(doc: Document, path: FmPath, value: unknown): string {
  const existing = doc.getIn(path, true)
  if (isScalar(existing)) {
    existing.value = value // keeps quoting/comment
  } else {
    doc.setIn(path, value)
  }
  return doc.toString()
}

export function setSeqItems(doc: Document, path: FmPath, items: unknown[]): string {
  const existing = doc.getIn(path, true)
  if (isSeq(existing)) {
    existing.items = items.map((v) => new Scalar(v))
  } else {
    doc.setIn(path, items)
  }
  return doc.toString()
}

export function deleteKey(doc: Document, path: FmPath): string {
  doc.deleteIn(path)
  return doc.toString()
}

export function addKey(doc: Document, parentPath: FmPath, key: string, value: unknown = ''): string {
  const parent = parentPath.length === 0 ? doc.contents : doc.getIn(parentPath, true)
  if (isMap(parent)) {
    parent.add(new Pair(new Scalar(key), value instanceof Object ? value : new Scalar(value)))
  } else if (parentPath.length === 0) {
    doc.contents = new YAMLMap() as unknown as Document['contents']
    ;(doc.contents as YAMLMap).add(new Pair(new Scalar(key), new Scalar(value)))
  } else {
    doc.setIn([...parentPath, key], value)
  }
  return doc.toString()
}

export function addChildMap(doc: Document, parentPath: FmPath, key: string): string {
  return addKey(doc, parentPath, key, new YAMLMap())
}

export function addChildSeq(doc: Document, parentPath: FmPath, key: string): string {
  return addKey(doc, parentPath, key, new YAMLSeq())
}

export function renameKey(doc: Document, path: FmPath, newKey: string): string {
  const parentPath = path.slice(0, -1)
  const parent = parentPath.length === 0 ? doc.contents : doc.getIn(parentPath, true)
  if (isMap(parent)) {
    const pair = parent.items.find((p) => String(scalarValue(p.key)) === String(path[path.length - 1]))
    if (pair && isPair(pair)) {
      if (isScalar(pair.key)) pair.key.value = newKey
      else pair.key = new Scalar(newKey)
    }
  }
  return doc.toString()
}

export function moveKey(doc: Document, path: FmPath, delta: -1 | 1): string {
  const parentPath = path.slice(0, -1)
  const parent = parentPath.length === 0 ? doc.contents : doc.getIn(parentPath, true)
  if (isMap(parent)) {
    const i = parent.items.findIndex((p) => String(scalarValue(p.key)) === String(path[path.length - 1]))
    const j = i + delta
    if (i >= 0 && j >= 0 && j < parent.items.length) {
      const [item] = parent.items.splice(i, 1)
      parent.items.splice(j, 0, item!)
    }
  }
  return doc.toString()
}

export function emptyDocument(): Document {
  const doc: Document = new Document()
  doc.contents = new YAMLMap()
  return doc
}
