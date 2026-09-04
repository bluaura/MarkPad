/**
 * Diagnostic: `RT_FILE=name.md npx vitest run test/debug-roundtrip.spec.ts` prints the blocks whose
 * canonical keys differ between the original and the engine output, plus the first byte difference.
 */
import { readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { describe, it } from 'vitest'
import { align, parseBlocks, roundTrip } from '../src/roundtrip'
import { splitFrontMatter } from '../src/frontmatter'
import { createHeadlessEditor } from './helpers/headless-editor'

const corpusDir = resolve(__dirname, '../../../corpus/roundtrip')
const file = process.env['RT_FILE']

describe.skipIf(!file)('debug round-trip', () => {
  it(`explains ${file}`, async () => {
    const editor = await createHeadlessEditor()
    const raw = readFileSync(join(corpusDir, file!), 'utf8').replace(/\r\n?/g, '\n')
    const { raw: fm, body } = splitFrontMatter(raw)
    const current = await editor.roundTripMarkdown(body)
    const a = parseBlocks(body)
    const b = parseBlocks(current)
    const items = align(
      a.map((x) => x.key),
      b.map((x) => x.key),
    )
    const lines: string[] = []
    let i = 0
    while (i < items.length) {
      const it = items[i]!
      if (it.a !== undefined && it.b !== undefined) {
        i++
        continue
      }
      const aOnly: number[] = []
      const bOnly: number[] = []
      while (i < items.length && !(items[i]!.a !== undefined && items[i]!.b !== undefined)) {
        const x = items[i]!
        if (x.a !== undefined) aOnly.push(x.a)
        if (x.b !== undefined) bOnly.push(x.b)
        i++
      }
      for (let k = 0; k < Math.max(aOnly.length, bOnly.length); k++) {
        const ai = aOnly[k]
        const bi = bOnly[k]
        lines.push(`--- mismatch #${k}`)
        if (ai !== undefined) {
          lines.push(`A[${ai}] ${a[ai]!.node.type}: ${body.slice(a[ai]!.start, a[ai]!.end).slice(0, 160).replace(/\n/g, '⏎')}`)
          lines.push(`   key: ${a[ai]!.key.slice(0, 400)}`)
        }
        if (bi !== undefined) {
          lines.push(`B[${bi}] ${b[bi]!.node.type}: ${current.slice(b[bi]!.start, b[bi]!.end).slice(0, 160).replace(/\n/g, '⏎')}`)
          lines.push(`   key: ${b[bi]!.key.slice(0, 400)}`)
        }
        if (ai !== undefined && bi !== undefined && a[ai]!.key.length > 400) {
          const ka = a[ai]!.key
          const kb = b[bi]!.key
          let d = 0
          while (d < ka.length && ka[d] === kb[d]) d++
          lines.push(`   first key diff @${d}: A…${ka.slice(Math.max(0, d - 80), d + 120)}`)
          lines.push(`                      B…${kb.slice(Math.max(0, d - 80), d + 120)}`)
        }
      }
    }
    const out = fm + roundTrip(body, current).text
    if (out !== raw) {
      let d = 0
      while (d < raw.length && raw[d] === out[d]) d++
      lines.push(`=== first byte diff @${d}: raw=${JSON.stringify(raw.slice(Math.max(0, d - 40), d + 60))}`)
      lines.push(`                          out=${JSON.stringify(out.slice(Math.max(0, d - 40), d + 60))}`)
    } else {
      lines.push('=== byte-identical')
    }
    console.log(lines.join('\n'))
    await editor.destroy()
  })
})
