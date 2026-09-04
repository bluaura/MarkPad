/**
 * Creates the same Crepe configuration main.ts uses, inside jsdom, so tests exercise the real
 * remark parser/serializer plus every Crepe schema extension (image-block, latex, table, …).
 */
import { Crepe } from '@milkdown/crepe'
import { editorViewCtx, parserCtx, serializerCtx } from '@milkdown/kit/core'

export interface HeadlessEditor {
  crepe: Crepe
  /** Parse markdown into the editor schema and serialize it back (what crepe.getMarkdown() does on load). */
  roundTripMarkdown(markdown: string): Promise<string>
  destroy(): Promise<void>
}

function installDomShims(): void {
  const g = globalThis as unknown as Record<string, unknown>
  if (!('ResizeObserver' in g)) {
    g['ResizeObserver'] = class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    }
  }
  if (!('IntersectionObserver' in g)) {
    g['IntersectionObserver'] = class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
      takeRecords(): unknown[] {
        return []
      }
    }
  }
  const proto = globalThis.Range?.prototype as unknown as Record<string, unknown> | undefined
  if (proto && !proto['getClientRects']) {
    proto['getClientRects'] = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] })
    proto['getBoundingClientRect'] = () => ({ x: 0, y: 0, width: 0, height: 0, top: 0, left: 0, right: 0, bottom: 0 })
  }
  const el = globalThis.Element?.prototype as unknown as Record<string, unknown> | undefined
  if (el && !el['getClientRects']) {
    el['getClientRects'] = () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] })
  }
  if (el && !el['scrollIntoView']) el['scrollIntoView'] = () => {}
  if (!('matchMedia' in g)) {
    g['matchMedia'] = () => ({ matches: false, addEventListener() {}, removeEventListener() {}, addListener() {}, removeListener() {} })
  }
}

export async function createHeadlessEditor(): Promise<HeadlessEditor> {
  installDomShims()
  const root = document.createElement('div')
  document.body.appendChild(root)

  const crepe = new Crepe({
    root,
    defaultValue: '',
    features: {
      [Crepe.Feature.Toolbar]: false,
      [Crepe.Feature.TopBar]: false,
      [Crepe.Feature.BlockEdit]: false,
      [Crepe.Feature.AI]: false,
      [Crepe.Feature.Placeholder]: false,
      [Crepe.Feature.Cursor]: false,
      [Crepe.Feature.LinkTooltip]: false,
      [Crepe.Feature.Latex]: true,
    },
  })
  await crepe.create()

  return {
    crepe,
    async roundTripMarkdown(markdown: string): Promise<string> {
      return crepe.editor.action((ctx) => {
        const parse = ctx.get(parserCtx)
        const serialize = ctx.get(serializerCtx)
        const doc = parse(markdown)
        // Mirror crepe.getMarkdown(): it serializes the current view state, so load the parsed doc into the view.
        const view = ctx.get(editorViewCtx)
        void view
        return serialize(doc)
      })
    },
    destroy: () => crepe.destroy().then(() => undefined),
  }
}
