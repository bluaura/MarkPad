import type { MilkdownPlugin } from '@milkdown/kit/ctx'
import { codeBlockSchema } from '@milkdown/kit/preset/commonmark'

/**
 * Keep the fenced-code info string beyond the language (e.g. ```` ```toml {file="hugo.toml"} ````).
 * Milkdown only models `language`; without this the `meta` part is silently dropped on serialize.
 */
export const codeBlockMetaSchema = codeBlockSchema.extendSchema((prev) => (ctx) => {
  const base = prev(ctx)
  return {
    ...base,
    attrs: {
      ...base.attrs,
      meta: { default: '', validate: 'string' },
    },
    parseMarkdown: {
      match: ({ type }) => type === 'code',
      runner: (state, node, type) => {
        const language = String(node['lang'] ?? '')
        const meta = node['meta'] == null ? '' : String(node['meta'])
        const value = String(node['value'] ?? '')
        state.openNode(type, { language, meta })
        if (value) state.addText(value)
        state.closeNode()
      },
    },
    toMarkdown: {
      match: (node) => node.type.name === 'code_block',
      runner: (state, node) => {
        state.addNode('code', undefined, node.content.firstChild?.text || '', {
          lang: node.attrs['language'],
          meta: node.attrs['meta'] ? node.attrs['meta'] : undefined,
        })
      },
    },
  }
})

export const codeMetaPlugin: MilkdownPlugin[] = [...codeBlockMetaSchema]
