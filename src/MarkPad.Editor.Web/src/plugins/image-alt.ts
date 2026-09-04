import { imageBlockSchema } from '@milkdown/kit/component/image-block'
import type { MilkdownPlugin } from '@milkdown/kit/ctx'

/**
 * Crepe's image-block stores the *aspect ratio* in the markdown `alt` slot and the caption in `title`,
 * so `![Big O graphs](x.png)` would be written back as `![1.00](x.png)`. That breaks round-trip
 * preservation (PRD G4) and alt editing (F-IMG-05). This extension keeps `alt` verbatim and never
 * persists the ratio (image sizing is P2, via `<img width>` per F-IMG-06).
 */
export const imageBlockAltSchema = imageBlockSchema.extendSchema((prev) => (ctx) => {
  const base = prev(ctx)
  return {
    ...base,
    attrs: {
      ...base.attrs,
      alt: { default: '', validate: 'string' },
    },
    parseDOM: [
      {
        tag: 'img[data-type="image-block"]',
        getAttrs: (dom) => {
          if (!(dom instanceof HTMLElement)) return false
          return {
            src: dom.getAttribute('src') || '',
            caption: dom.getAttribute('caption') || '',
            alt: dom.getAttribute('alt') || '',
            ratio: Number(dom.getAttribute('ratio') ?? 1),
          }
        },
      },
    ],
    parseMarkdown: {
      match: ({ type }) => type === 'image-block',
      runner: (state, node, type) => {
        state.addNode(type, {
          src: String(node['url'] ?? ''),
          caption: String(node['title'] ?? ''),
          alt: String(node['alt'] ?? ''),
          ratio: 1,
        })
      },
    },
    toMarkdown: {
      match: (node) => node.type.name === 'image-block',
      runner: (state, node) => {
        state.openNode('paragraph')
        state.addNode('image', undefined, undefined, {
          url: node.attrs['src'],
          alt: node.attrs['alt'] ?? '',
          title: node.attrs['caption'] || undefined,
        })
        state.closeNode()
      },
    },
  }
})

export const imageAltPlugin: MilkdownPlugin[] = [...imageBlockAltSchema]
