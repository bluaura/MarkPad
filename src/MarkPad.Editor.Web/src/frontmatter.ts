/**
 * YAML front matter is kept outside the editor until the front matter plugin lands (T-48).
 * `split` peels it off before loading; `join` puts the untouched raw block back at save time.
 * `raw` always includes the delimiters and the newline that follows the closing `---`.
 */
const FRONT_MATTER_RE = /^---[ \t]*\n(?:[\s\S]*?\n)?---[ \t]*(?:\n|$)/

export interface SplitDocument {
  raw: string
  body: string
}

export function splitFrontMatter(text: string): SplitDocument {
  const m = FRONT_MATTER_RE.exec(text)
  if (!m) return { raw: '', body: text }
  return { raw: m[0], body: text.slice(m[0].length) }
}

export function joinFrontMatter(raw: string, body: string): string {
  return raw + body
}
