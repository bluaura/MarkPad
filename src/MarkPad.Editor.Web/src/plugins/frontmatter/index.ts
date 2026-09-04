import type { MilkdownPlugin } from '@milkdown/kit/ctx'
import { $nodeSchema, $remark, $view } from '@milkdown/kit/utils'
import remarkFrontmatter from 'remark-frontmatter'
import { FrontmatterNodeView } from './view'

/**
 * YAML front matter as a real editor node (PRD F-VIEW-07, T-48). The remark plugin makes both the parser
 * and the serializer understand the mdast `yaml` node, so `---` blocks survive load → save byte-exact
 * when untouched (roundtrip.ts treats `yaml` as an ordinary top-level block).
 */
export const remarkFrontmatterPlugin = $remark('remark-frontmatter', () => remarkFrontmatter, ['yaml'])

export const frontmatterSchema = $nodeSchema('frontmatter', () => ({
  group: 'block',
  atom: true,
  selectable: true,
  draggable: false,
  isolating: true,
  defining: true,
  marks: '',
  attrs: {
    raw: { default: '', validate: 'string' },
  },
  parseDOM: [
    {
      tag: 'div[data-type="frontmatter"]',
      getAttrs: (dom) => (dom instanceof HTMLElement ? { raw: dom.getAttribute('data-raw') ?? '' } : false),
    },
  ],
  toDOM: (node) => ['div', { 'data-type': 'frontmatter', 'data-raw': String(node.attrs['raw'] ?? '') }],
  parseMarkdown: {
    match: ({ type }) => type === 'yaml',
    runner: (state, node, type) => {
      state.addNode(type, { raw: String(node['value'] ?? '') })
    },
  },
  toMarkdown: {
    match: (node) => node.type.name === 'frontmatter',
    runner: (state, node) => {
      state.addNode('yaml', undefined, String(node.attrs['raw'] ?? ''))
    },
  },
}))

export const frontmatterView = $view(frontmatterSchema.node, () => (node, view, getPos) => new FrontmatterNodeView(node, view, getPos))

export const frontmatterPlugin: MilkdownPlugin[] = [...remarkFrontmatterPlugin, ...frontmatterSchema, frontmatterView]
